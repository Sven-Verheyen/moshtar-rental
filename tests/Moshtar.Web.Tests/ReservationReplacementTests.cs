using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Nadat de klant gekozen heeft, vervangt de beheerder of medewerker in de reservatie het artikel met een tekort door het
/// gekozen alternatief: op een losse lijn met behoud van prijs, in een pakket enkel binnen het pakket met een notitie. Een
/// meerprijs komt als aparte lijn; alles komt in de historiek en het tekort verdwijnt.
/// </summary>
public sealed class ReservationReplacementTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Replacing_a_loose_line_keeps_its_price_and_the_shortage_disappears()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("vervangen-los");
        var user = await app.CreateUserAsync("vervangen-los", "medewerker@vervangen-los.test", UserRole.Staff);
        var castles = await CategoryAsync(scope, "Springkastelen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var anna = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day.AddDays(1), "Anna");
        await Blockouts(scope).CreateAsync(new(jungle, 1, day, null, "Lek"));
        Assert.Single(await Shortages(scope).GetAsync());

        var replaced = await Reservations(scope).ReplaceItemAsync(anna.Id, jungle, pirate, null, ReservationActor.User(user.Id));

        var line = Assert.Single(replaced.Lines);
        // Twee dagen aan de prijs van de jungle, niet van de piraat (2 × 150).
        Assert.Equal((pirate, (Guid?)null, "Piraat", 200m), (line.RentalItemId, line.BundleId, line.Description, line.UnitPrice));
        Assert.Equal(200m, replaced.TotalPrice);
        Assert.Empty(await Shortages(scope).GetAsync());

        var history = Assert.Single(await Reservations(scope).GetHistoryAsync(anna.Id), e => e.Kind == ReservationEventKind.ItemReplaced);
        Assert.Equal(("medewerker@vervangen-los.test", "Jungle vervangen door Piraat (zonder meerprijs)"), (history.UserEmail, history.Note));
    }

    [Fact]
    public async Task Replacing_in_a_bundle_keeps_the_bundle_price_adds_the_surcharge_line_and_occupies_the_replacement()
    {
        var day = TestReservations.FutureDay(1);
        await using var scope = await NewTenantAsync("vervangen-pakket");
        var user = await app.CreateUserAsync("vervangen-pakket", "beheerder@vervangen-pakket.test");
        var castles = await CategoryAsync(scope, "Springkastelen");
        var games = await CategoryAsync(scope, "Spellen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100, french: "Château Jungle"));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150, french: "Château Pirate"));
        var balls = await Items(scope).CreateAsync(Input("Ballenbad", games, dayPrice: 50));
        var party = await BundleAsync(scope, "Kinderfeest", 130, jungle, balls);
        var camille = await ReserveAsync(scope, new ReservationLineRequest(null, party, 1), day, day, "Camille", culture: "fr");
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 0, french: "Château Jungle"));
        await Alternatives(scope).MailAsync(camille.Id, jungle, [new ChosenAlternative(pirate, 40)], ReservationActor.User(user.Id));
        Assert.Equal(40m, Assert.Single(await Alternatives(scope).GetProposedAsync(camille.Id)).Surcharge);

        var replaced = await Reservations(scope).ReplaceItemAsync(camille.Id, jungle, pirate, 40, ReservationActor.User(user.Id));

        Assert.Equal(2, replaced.Lines.Count);
        var bundleLine = replaced.Lines[0];
        Assert.Equal((party, "Kinderfeest FR", 130m), (bundleLine.BundleId!.Value, bundleLine.Description, bundleLine.UnitPrice));
        var replacement = Assert.Single(bundleLine.Replacements);
        Assert.Equal((jungle, "Château Jungle", pirate, "Château Pirate"),
            (replacement.RentalItemId, replacement.RentalItemName, replacement.ReplacementRentalItemId, replacement.ReplacementName));
        var surcharge = replaced.Lines[1];
        Assert.Equal(((Guid?)null, (Guid?)null, 1, "Supplément Château Pirate au lieu de Château Jungle", 40m),
            (surcharge.RentalItemId, surcharge.BundleId, surcharge.Quantity, surcharge.Description, surcharge.UnitPrice));
        Assert.Equal(170m, replaced.TotalPrice);

        // Het tekort is weg; de reservatie bezet nu de piraat en niet meer de jungle.
        Assert.Empty(await Shortages(scope).GetAsync());
        Assert.Empty(await Alternatives(scope).GetProposedAsync(camille.Id));
        Assert.Equal([new ReservedItem(balls, "Ballenbad", 1, "Kinderfeest"), new ReservedItem(pirate, "Piraat", 1, "Kinderfeest")],
            await Alternatives(scope).GetReservedItemsAsync(camille.Id));
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 1, french: "Château Jungle"));
        Assert.Empty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day, day), [new ReservationLineRequest(jungle, null, 1)]));
        Assert.NotEmpty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day, day), [new ReservationLineRequest(pirate, null, 1)]));

        var history = Assert.Single(await Reservations(scope).GetHistoryAsync(camille.Id), e => e.Kind == ReservationEventKind.ItemReplaced);
        Assert.Equal(("beheerder@vervangen-pakket.test", "Jungle vervangen door Piraat in Kinderfeest (meerprijs € 40,00)"), (history.UserEmail, history.Note));

        // De klant krijgt een bijgewerkte bevestiging in zijn taal, met de notitie op de pakketlijn.
        var mail = Outbox.Sent.Last(m => m.To == "camille@example.test");
        Assert.Contains(camille.Number, mail.Subject);
        foreach (var body in new[] { mail.TextBody, WebUtility.HtmlDecode(mail.HtmlBody) })
        {
            Assert.Contains("1 × Kinderfeest FR (Château Jungle remplacé par Château Pirate) – 130,00 €", body);
            Assert.Contains("1 × Supplément Château Pirate au lieu de Château Jungle – 40,00 €", body);
            Assert.Contains("170,00 €", body);
        }

        // Een artikel dat binnen een gereserveerd pakket gebruikt wordt, kan niet verwijderd worden.
        var error = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).DeleteAsync(pirate));
        Assert.True(error.CanSwitchOff);
    }

    [Fact]
    public async Task Only_a_free_active_item_of_the_same_category_can_replace_and_otherwise_nothing_changes()
    {
        var day = TestReservations.FutureDay(2);
        await using var scope = await NewTenantAsync("vervangen-fout");
        var actor = ReservationActor.User((await app.CreateUserAsync("vervangen-fout", "fout@vervangen-fout.test")).Id);
        var castles = await CategoryAsync(scope, "Springkastelen");
        var games = await CategoryAsync(scope, "Spellen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var dragon = await Items(scope).CreateAsync(Input("Draak", castles, dayPrice: 150));
        var game = await Items(scope).CreateAsync(Input("Sjoelbak", games, dayPrice: 20));
        await Items(scope).SetActiveAsync(dragon, false);
        var dora = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day.AddDays(1), "Dora");
        // De piraat is op de tweede dag al gereserveerd: niet vrij in de volledige huurperiode.
        await ReserveAsync(scope, new ReservationLineRequest(pirate, null, 1), day.AddDays(1), day.AddDays(1), "Emma");
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 0));

        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, pirate, null, actor));
        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, dragon, null, actor));
        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, game, null, actor));
        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, jungle, null, actor));
        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, pirate, dragon, null, actor));

        var free = await Items(scope).CreateAsync(Input("Prinses", castles, dayPrice: 100));
        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, free, -5, actor));
        await Reservations(scope).ChangeStatusAsync(dora.Id, ReservationStatus.Cancelled, actor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reservations(scope).ReplaceItemAsync(dora.Id, jungle, free, null, actor));

        Assert.DoesNotContain(await Reservations(scope).GetHistoryAsync(dora.Id), e => e.Kind == ReservationEventKind.ItemReplaced);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var line = Assert.Single(db.Reservations.Single(r => r.Id == dora.Id).Lines);
        Assert.Equal(jungle, line.RentalItemId);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Staff)]
    public async Task Administrator_and_staff_can_replace_from_the_shortages_and_from_the_reservation_and_see_the_note(UserRole role)
    {
        var day = TestReservations.FutureDay(20 + (int)role);
        var client = await app.LoggedInClientAsync($"vervangen-{role}@hopsakee.test", role);
        Reservation affected, replaced;
        Guid pirate;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var castles = await CategoryAsync(scope, $"Vervangkastelen {role}");
            var jungle = await Items(scope).CreateAsync(Input($"Jungle {role}", castles, dayPrice: 100, stock: 2) with { Slug = $"vervang-jungle-{role}".ToLowerInvariant() });
            pirate = await Items(scope).CreateAsync(Input($"Piraat {role}", castles, dayPrice: 150, stock: 2) with { Slug = $"vervang-piraat-{role}".ToLowerInvariant() });
            var party = await BundleAsync(scope, $"Feest {role}", 130, jungle);
            affected = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, $"Getroffen{role}");
            replaced = await ReserveAsync(scope, new ReservationLineRequest(null, party, 1), day, day, $"Vervangen{role}");
            await Items(scope).SaveAsync(jungle, Input($"Jungle {role}", castles, dayPrice: 100, stock: 0) with { Slug = $"vervang-jungle-{role}".ToLowerInvariant() });
            await Reservations(scope).ReplaceItemAsync(replaced.Id, jungle, pirate, 25, ReservationActor.Website);
        }

        var shortages = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/tekorten"));
        Assert.Contains($"data-replace=\"{pirate}\"", shortages);
        Assert.Contains("Vervangen", shortages);

        var reservation = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties?nummer={replaced.Number}"));
        Assert.Contains($"1 × Feest {role} (Jungle {role} vervangen door Piraat {role})", reservation);
        Assert.Contains($"1 × Meerprijs Piraat {role} i.p.v. Jungle {role}", reservation);
        Assert.Contains("€ 155,00", reservation);
        Assert.Contains("Artikel vervangen", reservation);
    }

    private async Task<AsyncServiceScope> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        return await app.TenantScopeAsync(slug);
    }

    private static async Task<Guid> CategoryAsync(AsyncServiceScope scope, string name)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Slug = name.ToLowerInvariant(), Translations = [new Translation { Culture = "nl", Name = name }] };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static async Task<Guid> BundleAsync(AsyncServiceScope scope, string name, decimal dayPrice, params Guid[] itemIds)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle
        {
            Slug = name.ToLowerInvariant(), Pricing = new Pricing { DayPrice = dayPrice },
            Translations = [new Translation { Culture = "nl", Name = name }, new Translation { Culture = "fr", Name = name + " FR" }],
            Items = itemIds.Select(id => new BundleItem { RentalItemId = id, Quantity = 1 }).ToList(),
        };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    private static async Task<Reservation> ReserveAsync(
        AsyncServiceScope scope, ReservationLineRequest line, DateOnly start, DateOnly end, string firstName, string culture = "nl")
    {
        var result = await Reservations(scope).ReserveAsync(new ReservationRequest(
            new DateRange(start, end), [line],
            new ReservationCustomer(firstName, "Klant", $"{firstName.ToLowerInvariant()}@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, culture), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private static RentalItemInput Input(string name, Guid category, decimal dayPrice, int stock = 1, string? french = null) =>
        new([new RentalItemText("nl", name), .. french is null ? Array.Empty<RentalItemText>() : [new RentalItemText("fr", french)]],
            category, stock, dayPrice, null, NoSpecs);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IBlockoutAdministration Blockouts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBlockoutAdministration>();

    private static IReservationService Reservations(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationService>();

    private static IShortageOverview Shortages(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IShortageOverview>();

    private static IReservationAlternatives Alternatives(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationAlternatives>();
}
