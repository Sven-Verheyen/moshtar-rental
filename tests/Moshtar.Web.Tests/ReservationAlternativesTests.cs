using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Per getroffen reservatie in Tekorten de alternatieven: artikelen uit dezelfde categorie die aan staan en vrij zijn in de
/// volledige huurperiode, met het prijsverschil als hulp. De beheerder of medewerker mailt de aangevinkte alternatieven met
/// eventueel een meerprijs in de taal van de klant; de reservatie onthoudt wanneer en welke meerprijzen.
/// </summary>
public sealed class ReservationAlternativesTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Alternatives_are_the_free_active_items_of_the_same_category_with_the_price_difference_for_the_rental_period()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("alternatieven-lijst");
        var castles = await CategoryAsync(scope, "Springkastelen");
        var games = await CategoryAsync(scope, "Spellen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100, extraDayPrice: 50));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var princess = await Items(scope).CreateAsync(Input("Prinses", castles, dayPrice: 100));
        var dragon = await Items(scope).CreateAsync(Input("Draak", castles, dayPrice: 100));
        await Items(scope).CreateAsync(Input("Sjoelbak", games, dayPrice: 20));
        await Items(scope).SetActiveAsync(dragon, false);
        var anna = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day.AddDays(1), "Anna");
        // De prinses is op de tweede dag al gereserveerd: niet vrij in de volledige huurperiode.
        await ReserveAsync(scope, new ReservationLineRequest(princess, null, 1), day.AddDays(1), day.AddDays(1), "Bert");
        await Blockouts(scope).CreateAsync(new(jungle, 1, day, null, "Lek"));

        var alternatives = await Alternatives(scope).GetAsync(anna.Id, jungle);

        var only = Assert.Single(alternatives);
        // Twee dagen: piraat 150 + 150, jungle 100 + 50.
        Assert.Equal((pirate, "Piraat", 150m, (decimal?)null), (only.RentalItemId, only.Name, only.PriceDifference, only.Surcharge));
    }

    [Fact]
    public async Task The_customer_gets_the_checked_alternatives_in_his_language_with_photo_surcharge_and_a_link_to_the_item_page()
    {
        var day = TestReservations.FutureDay(1);
        await using var scope = await NewTenantAsync("alternatieven-mail");
        var user = await app.CreateUserAsync("alternatieven-mail", "medewerker@alternatieven-mail.test", UserRole.Staff);
        var castles = await CategoryAsync(scope, "Springkastelen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100, french: "Château Jungle"));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150, french: "Château Pirate"));
        var princess = await Items(scope).CreateAsync(Input("Prinses", castles, dayPrice: 100, french: "Château Princesse"));
        var dragon = await Items(scope).CreateAsync(Input("Draak", castles, dayPrice: 100, french: "Château Dragon"));
        await Items(scope).AddPhotosAsync(pirate, [new PhotoUpload("piraat.png", TestImages.Png(800, 600))]);
        var camille = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, "Camille", culture: "fr");
        await Blockouts(scope).CreateAsync(new(jungle, 1, day, null, "Lek"));
        Assert.Equal(3, (await Alternatives(scope).GetAsync(camille.Id, jungle)).Count);

        // De draak niet aangevinkt; de prinses met een meerprijs van 0 is zonder meerprijs.
        await Alternatives(scope).MailAsync(camille.Id, jungle,
            [new ChosenAlternative(pirate, 25), new ChosenAlternative(princess, 0)], ReservationActor.User(user.Id));

        var mail = Assert.Single(Outbox.Sent, m => m.To == "camille@example.test" && m.Subject.Contains("alternative"));
        Assert.Equal($"Une alternative pour Château Jungle dans votre réservation {camille.Number}", mail.Subject);
        Assert.Contains("Bonjour Camille", mail.TextBody);
        Assert.Contains("- Château Pirate – supplément de 25,00 €", mail.TextBody);
        Assert.Contains("- Château Princesse – sans supplément", mail.TextBody);
        Assert.Contains("Voir sur le site: https://alternatieven-mail.test/fr/louer/piraat", mail.TextBody);
        Assert.Contains("Voir sur le site: https://alternatieven-mail.test/fr/louer/prinses", mail.TextBody);
        Assert.DoesNotContain("Dragon", mail.TextBody);
        Assert.Matches("<img src=\"https://alternatieven-mail.test/[^\"]+\" alt=\"Château Pirate\"", WebUtility.HtmlDecode(mail.HtmlBody));
        Assert.Contains("href=\"https://alternatieven-mail.test/fr/louer/piraat\"", mail.HtmlBody);

        // De link opent de artikelpagina in het Frans.
        var page = await app.CreateClient("alternatieven-mail.test").GetAsync("/fr/louer/piraat");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Château Pirate", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task After_mailing_the_reservation_shows_when_and_by_whom_and_keeps_the_surcharges_for_replacing()
    {
        var day = TestReservations.FutureDay(2);
        await using var scope = await NewTenantAsync("alternatieven-bewaard");
        var user = await app.CreateUserAsync("alternatieven-bewaard", "beheerder@alternatieven-bewaard.test");
        var castles = await CategoryAsync(scope, "Springkastelen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var princess = await Items(scope).CreateAsync(Input("Prinses", castles, dayPrice: 80));
        var party = await BundleAsync(scope, "Kinderfeest", jungle);
        var bert = await ReserveAsync(scope, new ReservationLineRequest(null, party, 1), day, day, "Bert");
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 0));

        // Ook binnen een pakket is de hulp het verschil in artikelprijs.
        var alternatives = await Alternatives(scope).GetAsync(bert.Id, jungle);
        Assert.Equal([(pirate, 50m), (princess, -20m)], alternatives.Select(a => (a.RentalItemId, a.PriceDifference)));

        var before = DateTime.UtcNow;
        await Alternatives(scope).MailAsync(bert.Id, jungle, [new ChosenAlternative(pirate, 40), new ChosenAlternative(princess, null)], ReservationActor.User(user.Id));

        var mail = Assert.Single(Outbox.Sent, m => m.To == "bert@example.test" && m.Subject.Contains("alternatief"));
        Assert.Equal($"Een alternatief voor Jungle in je reservatie {bert.Number}", mail.Subject);
        Assert.Contains("- Piraat – meerprijs € 40,00", mail.TextBody);
        Assert.Contains("Bekijk op de website: https://alternatieven-bewaard.test/huren/piraat", mail.TextBody);

        var mailed = Assert.Single(await Reservations(scope).GetHistoryAsync(bert.Id), e => e.Kind == ReservationEventKind.AlternativesMailed);
        Assert.Equal("beheerder@alternatieven-bewaard.test", mailed.UserEmail);
        Assert.Equal("Jungle: Piraat (meerprijs € 40,00), Prinses (zonder meerprijs)", mailed.Note);
        Assert.InRange(mailed.OccurredAtUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));

        var shortage = Assert.Single(Assert.Single(await Shortages(scope).GetAsync()).Reservations);
        Assert.Equal(mailed.OccurredAtUtc, shortage.AlternativesMailed?.AtUtc);
        Assert.Equal("beheerder@alternatieven-bewaard.test", shortage.AlternativesMailed?.UserEmail);

        Assert.Equal([(jungle, pirate, (decimal?)40m), (jungle, princess, null)],
            (await Alternatives(scope).GetProposedAsync(bert.Id)).Select(p => (p.RentalItemId, p.AlternativeRentalItemId, p.Surcharge)));
        // Opnieuw openen toont de gekozen meerprijzen; opnieuw mailen vervangt ze.
        Assert.Equal([40m, null], (await Alternatives(scope).GetAsync(bert.Id, jungle)).Select(a => a.Surcharge));
        await Alternatives(scope).MailAsync(bert.Id, jungle, [new ChosenAlternative(princess, 10)], ReservationActor.User(user.Id));
        Assert.Equal([(jungle, princess, (decimal?)10m)],
            (await Alternatives(scope).GetProposedAsync(bert.Id)).Select(p => (p.RentalItemId, p.AlternativeRentalItemId, p.Surcharge)));
    }

    [Fact]
    public async Task Nothing_is_mailed_without_a_checked_alternative_with_a_negative_surcharge_or_with_an_item_that_is_not_free()
    {
        var day = TestReservations.FutureDay(3);
        await using var scope = await NewTenantAsync("alternatieven-fout");
        var castles = await CategoryAsync(scope, "Springkastelen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var dora = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, "Dora");
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 0));
        var actor = ReservationActor.User((await app.CreateUserAsync("alternatieven-fout", "fout@alternatieven-fout.test")).Id);

        await Assert.ThrowsAsync<ArgumentException>(() => Alternatives(scope).MailAsync(dora.Id, jungle, [], actor));
        await Assert.ThrowsAsync<ArgumentException>(() => Alternatives(scope).MailAsync(dora.Id, jungle, [new ChosenAlternative(pirate, -5)], actor));
        await ReserveAsync(scope, new ReservationLineRequest(pirate, null, 1), day, day, "Emma");
        await Assert.ThrowsAsync<ArgumentException>(() => Alternatives(scope).MailAsync(dora.Id, jungle, [new ChosenAlternative(pirate, null)], actor));

        Assert.DoesNotContain(Outbox.Sent, m => m.To == "dora@example.test" && m.Subject.Contains("alternatief"));
        Assert.DoesNotContain(await Reservations(scope).GetHistoryAsync(dora.Id), e => e.Kind == ReservationEventKind.AlternativesMailed);
    }

    [Fact]
    public async Task The_mail_template_can_be_changed_but_keeps_its_alternatives_and_has_a_preview_and_a_test_mail()
    {
        var day = TestReservations.FutureDay(4);
        await using var scope = await NewTenantAsync("alternatieven-sjabloon");
        var user = await app.CreateUserAsync("alternatieven-sjabloon", "sjabloon@alternatieven-sjabloon.test");
        var templates = scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>();
        Assert.Contains(MailKinds.All, k => k.Kind == MailKind.ReservationAlternatives);
        Assert.All(["nl", "fr", "en"], culture => Assert.Empty(MailKinds.ReservationAlternatives.Problems(MailKinds.ReservationAlternatives.Standard(culture))));

        var error = await Assert.ThrowsAsync<MailTemplateException>(() =>
            templates.PreviewAsync(MailKind.ReservationAlternatives, "nl", new MailTemplateText("Sorry", "Er is een probleem met {unavailableItem}.")));
        Assert.Equal(["{alternatives} ontbreekt in de tekst."], error.Problems);

        var preview = await templates.PreviewAsync(MailKind.ReservationAlternatives, "en",
            new MailTemplateText("{unavailableItem} for {firstName}", "Pick one:\n\n{alternatives}\n\n{reservationDetails}"));
        Assert.Equal("Bouncy castle Pirate for Alex", preview.Subject);
        Assert.Contains("- Bouncy castle Jungle – no extra charge", preview.TextBody);
        Assert.Matches(@"- Bouncy castle Princess – extra charge of €\s?25[.,]00", preview.TextBody);
        Assert.Contains("View on the website: https://alternatieven-sjabloon.test/en/rent/voorbeeld", preview.TextBody);
        Assert.Contains("Reservation number:", preview.TextBody);

        await templates.SendTestAsync(MailKind.ReservationAlternatives, "fr", MailKinds.ReservationAlternatives.Standard("fr"), user.Id);
        var test = Assert.Single(Outbox.Sent, m => m.To == "sjabloon@alternatieven-sjabloon.test");
        Assert.StartsWith("[Testmail] Une alternative pour", test.Subject);

        // Een aangepaste tekst gaat mee in de echte mail.
        await templates.SaveAsync(MailKind.ReservationAlternatives, "nl",
            new MailTemplateText("Ander voorstel voor {reservationNumber}", "Dag {firstName}, kies maar:\n\n{alternatives}"), user.Id);
        var castles = await CategoryAsync(scope, "Springkastelen");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", castles, dayPrice: 100));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", castles, dayPrice: 150));
        var fien = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, "Fien");
        await Items(scope).SaveAsync(jungle, Input("Jungle", castles, dayPrice: 100, stock: 0));
        await Alternatives(scope).MailAsync(fien.Id, jungle, [new ChosenAlternative(pirate, null)], ReservationActor.User(user.Id));
        var mail = Assert.Single(Outbox.Sent, m => m.To == "fien@example.test" && m.Subject.StartsWith("Ander voorstel"));
        Assert.Contains("Dag Fien, kies maar:", mail.TextBody);
        Assert.Contains("- Piraat – zonder meerprijs", mail.TextBody);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Staff)]
    public async Task Administrator_and_staff_see_the_alternatives_per_affected_reservation_in_the_shortages(UserRole role)
    {
        var day = TestReservations.FutureDay(10 + (int)role);
        var client = await app.LoggedInClientAsync($"alternatieven-{role}@hopsakee.test", role);
        Guid pirate;
        Reservation mailed;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var user = await app.CreateUserAsync("hopsakee", $"mailer-{role}@hopsakee.test");
            var castles = await CategoryAsync(scope, $"Kastelen {role}");
            var jungle = await Items(scope).CreateAsync(Input($"Jungle {role}", castles, dayPrice: 100, stock: 2) with { Slug = $"jungle-{role}".ToLowerInvariant() });
            pirate = await Items(scope).CreateAsync(Input($"Piraat {role}", castles, dayPrice: 135) with { Slug = $"piraat-{role}".ToLowerInvariant() });
            await Items(scope).AddPhotosAsync(pirate, [new PhotoUpload("piraat.png", TestImages.Png(800, 600))]);
            mailed = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, $"Gemaild{role}");
            await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day, $"Nieuw{role}");
            await Items(scope).SaveAsync(jungle, Input($"Jungle {role}", castles, dayPrice: 100, stock: 0) with { Slug = $"jungle-{role}".ToLowerInvariant() });
            await Alternatives(scope).MailAsync(mailed.Id, jungle, [new ChosenAlternative(pirate, 30)], ReservationActor.User(user.Id));
        }

        var response = await client.GetAsync("/admin/tekorten");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Piraat {role}", html);
        Assert.Contains("+ € 35,00", html);
        Assert.Contains($"data-alternative=\"{pirate}\"", html);
        Assert.Matches("<img [^>]*src=\"https://hopsakee.test/", html);
        Assert.Contains($"Alternatieven gemaild op", html);
        Assert.Contains($"door mailer-{role}@hopsakee.test", html);
        Assert.Contains("Mail versturen", html);

        var reservations = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties?nummer={mailed.Number}"));
        Assert.Contains("Alternatieven gemaild op", reservations);
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

    private static async Task<Guid> BundleAsync(AsyncServiceScope scope, string name, Guid itemId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle
        {
            Slug = name.ToLowerInvariant(), Pricing = new Pricing { DayPrice = 150 },
            Translations = [new Translation { Culture = "nl", Name = name }],
            Items = [new BundleItem { RentalItemId = itemId, Quantity = 1 }],
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
            new ReservationCustomer(firstName, "Klant", $"{firstName.ToLowerInvariant()}@example.test", null, new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, culture), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private static RentalItemInput Input(string name, Guid category, decimal dayPrice, decimal? extraDayPrice = null, int stock = 1, string? french = null) =>
        new([new RentalItemText("nl", name), .. french is null ? Array.Empty<RentalItemText>() : [new RentalItemText("fr", french)]],
            category, stock, dayPrice, extraDayPrice, NoSpecs);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IBlockoutAdministration Blockouts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBlockoutAdministration>();

    private static IReservationService Reservations(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationService>();

    private static IShortageOverview Shortages(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IShortageOverview>();

    private static IReservationAlternatives Alternatives(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationAlternatives>();
}
