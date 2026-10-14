using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder maakt artikelen aan en wijzigt ze in het back office: naam en beschrijving per taal, categorie,
/// kenmerken, voorraad, prijzen, slug, volgorde binnen de categorie, uitzetten en verwijderen.
/// </summary>
public sealed class RentalItemAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task An_item_is_created_with_only_a_name_in_the_default_language_and_the_site_shows_that_name_in_every_language()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-aanmaken");
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", " Springkasteel Jungle ")], castles));

            var created = await Items(scope).GetAsync(id);
            Assert.Equal("springkasteel-jungle", created!.Slug);
            Assert.True(created.IsActive);
            Assert.Equal("Springkasteel Jungle", Assert.Single(created.Input.Texts, t => t.Culture == "nl").Name);
        }
        var client = app.CreateClient("artikel-aanmaken.test");

        Assert.Contains("<h1>Springkasteel Jungle</h1>", await client.GetStringAsync("/huren/springkasteel-jungle"));
        Assert.Contains("<h1>Springkasteel Jungle</h1>", await client.GetStringAsync("/fr/louer/springkasteel-jungle"));
    }

    [Fact]
    public async Task A_missing_translation_shows_the_default_language_and_the_overview_says_which_translations_are_missing()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-vertalingen");
        await using (scope)
        {
            var jungle = await Items(scope).CreateAsync(Input(
                [new("nl", "Jungle", "Met palmbomen."), new("fr", "Jungle FR"), new("en", "Jungle EN", "With palm trees.")], castles));
            var pirate = await Items(scope).CreateAsync(Input([new("nl", "Piraat")], castles));
            var complete = await Items(scope).CreateAsync(Input([new("nl", "Draak"), new("fr", "Dragon"), new("en", "Dragon")], castles));

            var overview = await Items(scope).GetAllAsync();

            Assert.Equal(["fr"], overview.Single(i => i.Id == jungle).MissingTranslations);
            Assert.Equal(["fr", "en"], overview.Single(i => i.Id == pirate).MissingTranslations);
            Assert.Empty(overview.Single(i => i.Id == complete).MissingTranslations);
        }
        var client = app.CreateClient("artikel-vertalingen.test");

        var french = await client.GetStringAsync("/fr/louer/jungle");
        Assert.Contains("<h1>Jungle FR</h1>", french);
        Assert.Contains("Met palmbomen.", french);
        Assert.Contains("<h1>Piraat</h1>", await client.GetStringAsync("/en/rent/piraat"));
    }

    [Fact]
    public async Task An_item_without_a_name_in_the_default_language_is_refused()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-zonder-naam");
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<RentalItemException>(() =>
                Items(scope).CreateAsync(Input([new("nl", " "), new("fr", "Jungle"), new("en", "", "Only a description")], castles)));

            Assert.Equal(["De naam in het Nederlands is leeg.", "Vul ook de naam in het Engels in, of laat de beschrijving leeg."], refused.Problems);
            Assert.Empty(await Items(scope).GetAllAsync());
        }
    }

    [Fact]
    public async Task Category_specs_stock_and_prices_are_saved_and_the_specs_are_optional()
    {
        var (scope, castles, games) = await NewTenantAsync("artikel-wijzigen");
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
            var specs = new ItemSpecs(500, 400, 350, 3, 12, 8, true);

            var shortages = await Items(scope).SaveAsync(id, Input([new("nl", "Jungle"), new("fr", "Jungle")], games, stock: 3,
                dayPrice: 150, extraDayPrice: 75, specs: specs, slug: "jungle"));

            Assert.Empty(shortages);
            var saved = (await Items(scope).GetAsync(id))!.Input;
            Assert.Equal((games, 3, 150m, (decimal?)75m, specs), (saved.CategoryId, saved.Stock, saved.DayPrice, saved.ExtraDayPrice, saved.Specs));
        }
        var html = WebUtility.HtmlDecode(await app.CreateClient("artikel-wijzigen.test").GetStringAsync("/huren/jungle"));
        Assert.Contains("<dt>Max. personen</dt>", html);
        Assert.Contains("150,00", html);
    }

    [Theory]
    [InlineData(-1, 100, "De voorraad kan niet negatief zijn.")]
    [InlineData(1, -5, "De dagprijs kan niet negatief zijn.")]
    public async Task A_negative_stock_or_price_is_refused(int stock, decimal dayPrice, string problem)
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-negatief-" + (stock < 0 ? "voorraad" : "prijs"));
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<RentalItemException>(() =>
                Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles, stock: stock, dayPrice: dayPrice)));

            Assert.Equal([problem], refused.Problems);
        }
    }

    [Fact]
    public async Task A_slug_made_from_the_name_gets_a_number_when_it_is_taken_and_a_chosen_slug_must_be_free()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-slug");
        await using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Bundles.Add(new Bundle { Slug = "feestpakket", Translations = [T("nl", "Feestpakket")] });
            await db.SaveChangesAsync();
            var first = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));

            var second = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));

            Assert.Equal("jungle-2", (await Items(scope).GetAsync(second))!.Slug);
            foreach (var taken in new[] { "jungle", "feestpakket" })
            {
                var refused = await Assert.ThrowsAsync<RentalItemException>(() =>
                    Items(scope).SaveAsync(second, Input([new("nl", "Jungle")], castles, slug: taken)));
                Assert.Equal([$"De slug \"{taken}\" hoort al bij een ander artikel of pakket; kies een andere."], refused.Problems);
            }
            var invalid = await Assert.ThrowsAsync<RentalItemException>(() =>
                Items(scope).SaveAsync(first, Input([new("nl", "Jungle")], castles, slug: "Jungle Groot")));
            Assert.Contains("mag alleen kleine letters", Assert.Single(invalid.Problems));
        }
    }

    [Fact]
    public async Task An_old_item_slug_redirects_permanently_to_the_new_one_in_every_language()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-doorsturen");
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
            await Items(scope).SaveAsync(id, Input([new("nl", "Jungle")], castles, slug: "jungle-groot"));
            await Items(scope).SaveAsync(id, Input([new("nl", "Jungle")], castles, slug: "springkasteel-jungle"));
        }
        var client = app.CreateClient("artikel-doorsturen.test");

        foreach (var (old, current) in new[]
                 {
                     ("/huren/jungle", "/huren/springkasteel-jungle"),
                     ("/huren/jungle-groot", "/huren/springkasteel-jungle"),
                     ("/fr/louer/jungle", "/fr/louer/springkasteel-jungle"),
                 })
        {
            var response = await client.GetAsync(old + "?van=2030-06-01&tot=2030-06-02");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal(current + "?van=2030-06-01&tot=2030-06-02", response.Headers.Location?.OriginalString);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/huren/springkasteel-jungle")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/onbekend")).StatusCode);
    }

    [Fact]
    public async Task An_old_slug_that_another_item_takes_shows_that_item()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-oude-slug-hergebruikt");
        await using (scope)
        {
            var jungle = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
            await Items(scope).SaveAsync(jungle, Input([new("nl", "Jungle")], castles, slug: "oerwoud"));
            await Items(scope).CreateAsync(Input([new("nl", "Nieuwe jungle")], castles, slug: "jungle"));
        }
        var client = app.CreateClient("artikel-oude-slug-hergebruikt.test");

        Assert.Contains("<h1>Nieuwe jungle</h1>", await client.GetStringAsync("/huren/jungle"));
    }

    [Fact]
    public async Task New_items_come_last_and_the_category_page_follows_the_order_the_administrator_chose()
    {
        var (scope, castles, games) = await NewTenantAsync("artikel-volgorde");
        await using (scope)
        {
            var anaconda = await Items(scope).CreateAsync(Input([new("nl", "Anaconda")], castles));
            var zebra = await Items(scope).CreateAsync(Input([new("nl", "Zebra")], castles));
            var game = await Items(scope).CreateAsync(Input([new("nl", "Spel")], games));
            var moved = await Items(scope).CreateAsync(Input([new("nl", "Bever")], games));
            // Naar een andere categorie: achteraan in die categorie.
            await Items(scope).SaveAsync(moved, Input([new("nl", "Bever")], castles));
            Assert.Equal([anaconda, zebra, moved, game], (await Items(scope).GetAllAsync()).Select(i => i.Id));

            await Items(scope).ReorderAsync(castles, [moved, zebra, anaconda]);

            Assert.Equal([moved, zebra, anaconda, game], (await Items(scope).GetAllAsync()).Select(i => i.Id));
            await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).ReorderAsync(castles, [zebra, anaconda]));
        }
        var html = await app.CreateClient("artikel-volgorde.test").GetStringAsync("/springkastelen");

        var cards = new[] { "Bever", "Zebra", "Anaconda" }.Select(name => html.IndexOf($">{name}</a></h3>", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, cards);
        Assert.Equal(cards.Order(), cards);
    }

    [Fact]
    public async Task A_switched_off_item_disappears_from_the_site_and_cannot_be_reserved_but_existing_reservations_stay()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-uitzetten");
        Guid reservationId;
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles, stock: 2));
            var bundle = await AddBundleAsync(scope, id);
            reservationId = (await ReserveAsync(scope, id, TestReservations.FutureDay())).Id;

            await Items(scope).SetActiveAsync(id, false);

            Assert.False((await Items(scope).GetAsync(id))!.IsActive);
            var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
            await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(scope, id, TestReservations.FutureDay(3)));
            // Ook niet via een pakket.
            var viaBundle = await reservations.ReserveAsync(Request(TestReservations.FutureDay(5), new ReservationLineRequest(null, bundle, 1)), ReservationActor.Website);
            Assert.Null(viaBundle.Reservation);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var kept = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);
            Assert.Equal((ReservationStatus.Confirmed, 100m, id), (kept.Status, kept.TotalPrice, kept.Lines.Single().RentalItemId));
        }
        var client = app.CreateClient("artikel-uitzetten.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/jungle")).StatusCode);
        Assert.DoesNotContain(">Jungle</a>", await client.GetStringAsync("/springkastelen"));
    }

    [Fact]
    public async Task An_item_that_was_never_reserved_is_deleted_and_its_page_disappears()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-verwijderen");
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
            await Items(scope).SaveAsync(id, Input([new("nl", "Jungle")], castles, slug: "oerwoud"));

            await Items(scope).DeleteAsync(id);

            Assert.Null(await Items(scope).GetAsync(id));
        }
        var client = app.CreateClient("artikel-verwijderen.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/oerwoud")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/jungle")).StatusCode);
    }

    [Fact]
    public async Task An_item_that_was_reserved_or_is_in_a_bundle_cannot_be_deleted_and_switching_off_is_proposed()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-niet-verwijderen");
        await using (scope)
        {
            var reserved = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
            await ReserveAsync(scope, reserved, TestReservations.FutureDay());
            var inBundle = await Items(scope).CreateAsync(Input([new("nl", "Piraat")], castles));
            await AddBundleAsync(scope, inBundle);

            var refused = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).DeleteAsync(reserved));
            Assert.True(refused.CanSwitchOff);
            Assert.Equal(["Het artikel \"Jungle\" werd al gereserveerd en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite en blijven de reservaties ongewijzigd."], refused.Problems);

            refused = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).DeleteAsync(inBundle));
            Assert.True(refused.CanSwitchOff);
            Assert.Contains("zit in het pakket \"Feestpakket\"", Assert.Single(refused.Problems));
            Assert.Equal(2, (await Items(scope).GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task An_item_in_a_cart_cannot_be_deleted_and_the_cart_line_says_it_is_no_longer_available_once_switched_off()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-winkelkar");
        Guid id;
        await using (scope)
            id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles));
        var client = app.CreateClient("artikel-winkelkar.test");
        var day = TestReservations.FutureDay().ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Redirect, (await WebsiteCart.AddAsync(client, $"/huren/jungle?van={day}&tot={day}")).StatusCode);

        await using (scope = await app.TenantScopeAsync("artikel-winkelkar"))
        {
            var refused = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).DeleteAsync(id));
            Assert.True(refused.CanSwitchOff);
            Assert.Contains("ligt in de winkelkar van een klant", Assert.Single(refused.Problems));
            await Items(scope).SetActiveAsync(id, false);
        }

        Assert.Contains("Dit artikel is niet meer te huren.", await client.GetStringAsync("/winkelkar"));
    }

    [Fact]
    public async Task A_stock_below_what_is_reserved_is_saved_with_a_warning_per_day_from_today()
    {
        var (scope, castles, _) = await NewTenantAsync("artikel-voorraad");
        await using (scope)
        {
            var id = await Items(scope).CreateAsync(Input([new("nl", "Jungle")], castles, stock: 2));
            var day = TestReservations.FutureDay();
            var first = await ReserveAsync(scope, id, day, day.AddDays(1));
            var second = await ReserveAsync(scope, id, day.AddDays(1), day.AddDays(2));
            // Een pakket met het artikel telt ook mee.
            var bundle = await AddBundleAsync(scope, id);
            var third = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(
                Request(day.AddDays(10), new ReservationLineRequest(null, bundle, 1)), ReservationActor.Website);

            var shortages = await Items(scope).SaveAsync(id, Input([new("nl", "Jungle")], castles, stock: 0));

            Assert.Equal(0, (await Items(scope).GetAsync(id))!.Input.Stock);
            Assert.Equal([day, day.AddDays(1), day.AddDays(2), day.AddDays(10)], shortages.Select(s => s.Day));
            var busiest = shortages[1];
            Assert.Equal((0, 2, 2), (busiest.Rentable, busiest.Reserved, busiest.Missing));
            Assert.Equal(new[] { first.Id, second.Id }.Order(), busiest.ReservationIds.Order());
            Assert.Equal([third.Reservation!.Id], shortages[3].ReservationIds);

            Assert.Empty(await Items(scope).SaveAsync(id, Input([new("nl", "Jungle")], castles, stock: 2)));
        }
    }

    [Fact]
    public async Task Administrator_sees_the_items_with_their_missing_translations()
    {
        var client = await app.LoggedInClientAsync("artikelen-beheerder@hopsakee.test", UserRole.Administrator);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Items(scope).CreateAsync(Input([new("nl", "Overzichtskasteel")], null, slug: "overzichtskasteel"));

        var page = await client.GetAsync("/admin/artikelen");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Overzichtskasteel", html);
        Assert.Contains("Vertaling ontbreekt: FR, EN", html);
        Assert.Contains("Nieuw artikel", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/artikelen/nieuw")).StatusCode);
    }

    [Theory]
    [InlineData("/admin/artikelen")]
    [InlineData("/admin/artikelen/nieuw")]
    public async Task Staff_cannot_manage_items(string path)
    {
        var client = await app.LoggedInClientAsync($"artikelen-medewerker-{path.Length}@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<Reservation> ReserveAsync(AsyncServiceScope scope, Guid itemId, DateOnly start, DateOnly? end = null)
    {
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(
            Request(start, new ReservationLineRequest(itemId, null, 1), end), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private static ReservationRequest Request(DateOnly start, ReservationLineRequest line, DateOnly? end = null) => new(
        new DateRange(start, end ?? start),
        [line],
        new ReservationCustomer("Test", "Klant", "klant@example.test", null, new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
        DeliveryMethod.Pickup, null, null, "nl");

    private static async Task<Guid> AddBundleAsync(AsyncServiceScope scope, Guid itemId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle
        {
            Slug = "feestpakket", Pricing = new Pricing { DayPrice = 200 }, Translations = [T("nl", "Feestpakket")],
            Items = [new BundleItem { RentalItemId = itemId, Quantity = 1 }],
        };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    /// <summary>Een nieuwe verhuurder met de categorieën Springkastelen en Spellen.</summary>
    private async Task<(AsyncServiceScope Scope, Guid Castles, Guid Games)> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] };
        var games = new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen"), T("fr", "Jeux"), T("en", "Games")] };
        db.Categories.AddRange(castles, games);
        await db.SaveChangesAsync();
        return (scope, castles.Id, games.Id);
    }

    private static RentalItemInput Input(IReadOnlyList<RentalItemText> texts, Guid? categoryId, int stock = 1, string? slug = null,
        decimal dayPrice = 100, decimal? extraDayPrice = null, ItemSpecs? specs = null) =>
        new(texts, categoryId, stock, dayPrice, extraDayPrice, specs ?? NoSpecs, slug);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
