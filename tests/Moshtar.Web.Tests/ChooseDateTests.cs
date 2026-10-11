using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De klant kiest een huurperiode met "Kies je datum" en ziet meteen wat vrij is, op de home en op de pagina van een
/// categorie. Op de pagina van een artikel ziet hij of het vrij is en wat het kost, en pas dan het reservatieformulier.
/// </summary>
public sealed class ChooseDateTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task With_a_rental_period_everything_free_comes_first_and_the_rest_is_marked_not_free()
    {
        var tenant = await NewTenantAsync("datum-lijst", bufferDaysAfter: 1);
        var day = TestReservations.FutureDay(10);
        await ReserveAsync("datum-lijst", tenant.Pirate, day.AddDays(-1));
        await BlockAsync("datum-lijst", tenant.Mill, day);

        var html = await PageAsync("datum-lijst", $"/?van={day:yyyy-MM-dd}");

        // Het piratenkasteel is de dag ervoor gereserveerd; de bufferdag erna raakt de gekozen dag.
        // De molen is geblokkeerd, en het pakket met de molen dus ook.
        Assert.Contains($"<h2>Ons aanbod voor {day.ToString("d MMMM yyyy", Dutch)}</h2>", html);
        Assert.Contains("<p class=\"muted\">1 van de 4 vrij</p>", html);
        var order = new[] { "Prinsessenkasteel", "Piratenkasteel", "Molen", "Feestpakket" }.Select(name => html.IndexOf($">{name}</a></h3>")).ToList();
        Assert.All(order, position => Assert.True(position > 0));
        Assert.True(order[0] < order[1] && order[0] < order[2] && order[0] < order[3], "Het vrije artikel staat bovenaan.");
        Assert.Equal(3, Count(html, "<p class=\"badge badge-busy\">Niet vrij op deze datum</p>"));
        Assert.DoesNotContain("Niet vrij", html[..order[0]]);
    }

    [Fact]
    public async Task Without_a_date_the_home_shows_the_categories_and_popular_items()
    {
        await NewTenantAsync("datum-home");

        var html = await PageAsync("datum-home", "/");

        Assert.Contains("<h2>Onze categorieën</h2>", html);
        Assert.Contains("<a class=\"category-tile\" href=\"/springkastelen\">", html);
        Assert.Contains("<a class=\"category-tile\" href=\"/spellen\">", html);
        Assert.Contains("<h2>Populair</h2>", html);
        Assert.Contains(">Feestpakket</a></h3>", html);
        Assert.Contains("<form id=\"datum\" class=\"period-form\" method=\"get\" action=\"/#aanbod\">", html);
        Assert.DoesNotContain("Niet vrij", html);
        Assert.DoesNotContain("noindex", html);
    }

    [Fact]
    public async Task Each_category_has_its_own_page_on_its_slug_in_each_language()
    {
        await NewTenantAsync("datum-categorie", castlesIntro: "Onze kastelen zijn proper.\n\nWe zetten ze zelf op.");
        var client = app.CreateClient("datum-categorie.test");

        var dutch = WebUtility.HtmlDecode(await client.GetStringAsync("/springkastelen"));
        var french = WebUtility.HtmlDecode(await client.GetStringAsync("/fr/chateaux-gonflables"));

        Assert.Contains("<h1>Springkastelen</h1>", dutch);
        Assert.Contains("<p class=\"intro\">Onze kastelen zijn proper.</p>", dutch);
        Assert.Contains("<p class=\"intro\">We zetten ze zelf op.</p>", dutch);
        Assert.Contains(">Piratenkasteel</a></h3>", dutch);
        Assert.DoesNotContain(">Molen</a></h3>", dutch);
        Assert.DoesNotContain(">Feestpakket</a></h3>", dutch);
        Assert.Contains("<link rel=\"canonical\" href=\"https://datum-categorie.test/springkastelen\" />", dutch);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"https://datum-categorie.test/en/bouncy-castles\" />", dutch);
        Assert.Contains("<h1>Châteaux gonflables</h1>", french);
        Assert.Contains("<a class=\"button cta\" href=\"#datum\">", dutch);

        // Een slug uit een andere taal stuurt door naar de slug in de taal van de URL, met de huurperiode erbij.
        var wrongLanguage = await client.GetAsync("/fr/springkastelen?van=2030-01-01");
        Assert.Equal(HttpStatusCode.MovedPermanently, wrongLanguage.StatusCode);
        Assert.Equal("/fr/chateaux-gonflables?van=2030-01-01", wrongLanguage.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/bestaat-niet")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/fr/bestaat-niet")).StatusCode);
    }

    [Fact]
    public async Task A_changed_slug_is_the_new_address_of_the_category()
    {
        var tenant = await NewTenantAsync("datum-slug");
        await using (var scope = await app.TenantScopeAsync("datum-slug"))
        {
            var user = await app.CreateUserAsync("datum-slug", "beheerder@datum-slug.test");
            await scope.ServiceProvider.GetRequiredService<IWebsiteTextAdministration>()
                .SaveCategoryAsync(tenant.Castles, "nl", "Luchtkastelen", "luchtkastelen", "", user.Id);
        }
        var client = app.CreateClient("datum-slug.test");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/luchtkastelen"));

        Assert.Contains("<h1>Luchtkastelen</h1>", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/springkastelen")).StatusCode);
    }

    [Fact]
    public async Task On_a_category_page_with_a_rental_period_the_free_items_come_first()
    {
        var tenant = await NewTenantAsync("datum-cat-periode");
        var day = TestReservations.FutureDay(20);
        await ReserveAsync("datum-cat-periode", tenant.Pirate, day);

        var html = await PageAsync("datum-cat-periode", $"/springkastelen?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}");

        Assert.Contains("<p class=\"muted\">1 van de 2 vrij</p>", html);
        Assert.True(html.IndexOf(">Prinsessenkasteel</a></h3>") < html.IndexOf(">Piratenkasteel</a></h3>"));
        Assert.Equal(1, Count(html, "Niet vrij op deze datum"));
        Assert.Contains($"value=\"{day:yyyy-MM-dd}\"", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/springkastelen")]
    [InlineData("/huren/prinses")]
    public async Task A_page_with_a_rental_period_is_not_indexed_and_points_to_the_page_without_date(string path)
    {
        await NewTenantAsync("datum-zoekmachine");
        var day = TestReservations.FutureDay(30);

        var withDate = await PageAsync("datum-zoekmachine", $"{path}?van={day:yyyy-MM-dd}");
        var withoutDate = await PageAsync("datum-zoekmachine", path);

        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", withDate);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://datum-zoekmachine.test{path}\" />", withDate);
        Assert.DoesNotContain("noindex", withoutDate);
    }

    [Theory]
    [InlineData(5, 3, "De einddag ligt voor de begindag. Kies een einddag op of na de begindag.")]
    [InlineData(-400, -400, "Die datum ligt in het verleden. Kies een dag vanaf vandaag.")]
    [InlineData(0, 31, "Je kan hoogstens 31 dagen na elkaar huren. Kies een kortere huurperiode of neem contact met ons op.")]
    [InlineData(400, 400, "Zo ver vooruit kan je nog niet reserveren. Kies een dag binnen de twee jaar.")]
    public async Task An_impossible_rental_period_gives_a_clear_message(int startOffset, int endOffset, string message)
    {
        await NewTenantAsync("datum-fout");
        var (start, end) = (TestReservations.FutureDay(startOffset), TestReservations.FutureDay(endOffset));

        foreach (var path in new[] { "/", "/springkastelen", "/huren/prinses" })
        {
            var html = await PageAsync("datum-fout", $"{path}?van={start:yyyy-MM-dd}&tot={end:yyyy-MM-dd}");

            Assert.Contains($"<p class=\"alert error\" role=\"alert\">{message}</p>", html);
            Assert.DoesNotContain("Niet vrij", html);
            Assert.DoesNotContain("name=\"Form.FirstName\"", html);
        }
    }

    [Fact]
    public async Task An_unreadable_date_gives_a_clear_message()
    {
        await NewTenantAsync("datum-onleesbaar");

        var html = await PageAsync("datum-onleesbaar", "/?van=morgen");

        Assert.Contains("Die datum kunnen we niet lezen. Kies een dag in de kalender.", html);
    }

    [Fact]
    public async Task Links_to_an_item_take_the_rental_period_along()
    {
        await NewTenantAsync("datum-links");
        var day = TestReservations.FutureDay(40);
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(2):yyyy-MM-dd}";

        var home = await PageAsync("datum-links", "/" + query);
        var category = await PageAsync("datum-links", "/springkastelen" + query);
        var item = await PageAsync("datum-links", "/huren/prinses" + query);

        Assert.Contains($"<a href=\"/huren/prinses{query}\">Prinsessenkasteel</a>", home);
        Assert.Contains($"<a href=\"/huren/prinses{query}\">Prinsessenkasteel</a>", category);
        Assert.Contains($"<a href=\"/springkastelen{query}#aanbod\">← Terug naar Springkastelen</a>", item);
    }

    [Fact]
    public async Task The_item_page_shows_availability_and_total_price_before_the_form()
    {
        await NewTenantAsync("datum-prijs");
        var day = TestReservations.FutureDay(50);

        var html = await PageAsync("datum-prijs", $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day.AddDays(2):yyyy-MM-dd}");

        // Eerste dag 120 euro, twee extra dagen aan 60 euro.
        var period = $"{day.ToString("d MMMM", Dutch)} – {day.AddDays(2).ToString("d MMMM yyyy", Dutch)}";
        if (day.Year != day.AddDays(2).Year) period = $"{day.ToString("d MMMM yyyy", Dutch)} – {day.AddDays(2).ToString("d MMMM yyyy", Dutch)}";
        Assert.Contains($"<strong>Vrij op {period}</strong>", html);
        Assert.Contains("Totaal: € 240,00 <span class=\"muted\">voor 3 dagen, per stuk</span>", html);
        Assert.True(html.IndexOf("Totaal: € 240,00") < html.IndexOf("name=\"Form.FirstName\""));
        Assert.Contains($"name=\"Form.StartDate\" value=\"{day:yyyy-MM-dd}\"", html);
        Assert.Contains($"name=\"Form.EndDate\" value=\"{day.AddDays(2):yyyy-MM-dd}\"", html);
        Assert.Contains("<dt>Afmetingen</dt>", html);
        Assert.Contains("<dd>5 × 4 × 3,5 m</dd>", html);
        Assert.Contains("<dd>3 – 12 jaar</dd>", html);
        Assert.Contains("<dt>Stroom nodig</dt>", html);
        Assert.Contains("<dd>Ja</dd>", html);
        Assert.Contains("<dd>Nee</dd>", await PageAsync("datum-prijs", "/huren/piraat"));
    }

    [Fact]
    public async Task Without_a_date_the_item_page_asks_for_the_date_first()
    {
        await NewTenantAsync("datum-eerst");

        var html = await PageAsync("datum-eerst", "/huren/prinses");

        Assert.Contains("Kies je datum, dan zie je meteen of het vrij is en wat het kost.", html);
        Assert.Contains("<form id=\"datum\" class=\"period-form\" method=\"get\" action=\"/huren/prinses#reserveren\">", html);
        Assert.DoesNotContain("name=\"Form.FirstName\"", html);
    }

    [Fact]
    public async Task When_the_item_is_not_free_there_is_no_form_but_a_way_to_choose_another_date()
    {
        var tenant = await NewTenantAsync("datum-bezet");
        var day = TestReservations.FutureDay(60);
        await ReserveAsync("datum-bezet", tenant.Pirate, day);

        var html = await PageAsync("datum-bezet", $"/huren/piraat?van={day:yyyy-MM-dd}");

        Assert.Contains($"<strong>Niet vrij op {day.ToString("d MMMM yyyy", Dutch)}</strong>", html);
        Assert.Contains("Dit is al gereserveerd op (een deel van) deze huurperiode.", html);
        Assert.Contains("<a class=\"button\" href=\"/huren/piraat#reserveren\">Andere datum kiezen</a>", html);
        Assert.Contains($"<a href=\"/?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}#aanbod\">Bekijk wat wel vrij is</a>", html);
        Assert.DoesNotContain("name=\"Form.FirstName\"", html);
    }

    [Fact]
    public async Task A_customer_reserves_through_the_new_item_page()
    {
        await NewTenantAsync("datum-reserveren");
        var client = app.CreateClient("datum-reserveren.test");
        var day = TestReservations.FutureDay(70);
        var path = $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";

        var form = await client.GetStringAsync(path);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "reservation",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Form.StartDate"] = BackOffice.Field(form, "Form.StartDate"),
            ["Form.EndDate"] = BackOffice.Field(form, "Form.EndDate"),
            ["Form.Quantity"] = "1",
            ["Form.FirstName"] = "Nina",
            ["Form.LastName"] = "Datum",
            ["Form.Email"] = "nina@example.test",
            ["Form.Street"] = "Kerkstraat 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Delivery",
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("is bevestigd. Totaal: € 180,00.", html);
        await using var scope = await app.TenantScopeAsync("datum-reserveren");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations
            .SingleAsync(r => r.Customer!.Email == "nina@example.test");
        Assert.Equal((day, day.AddDays(1), 180m), (reservation.StartDate, reservation.EndDate, reservation.TotalPrice));
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact]
    public async Task A_posted_rental_period_beyond_the_limits_is_refused_before_any_availability_is_calculated()
    {
        await NewTenantAsync("datum-post");
        var client = app.CreateClient("datum-post.test");
        var day = TestReservations.FutureDay(80);
        var path = $"/huren/prinses?van={day:yyyy-MM-dd}";

        var form = await client.GetStringAsync(path);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "reservation",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Form.StartDate"] = day.ToString("yyyy-MM-dd"),
            ["Form.EndDate"] = "9999-12-31",
            ["Form.Quantity"] = "1",
            ["Form.FirstName"] = "Max",
            ["Form.LastName"] = "Lang",
            ["Form.Email"] = "max@example.test",
            ["Form.Street"] = "Kerkstraat 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Pickup",
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("Een huurperiode is hoogstens 31 dagen.", html);
        await using var scope = await app.TenantScopeAsync("datum-post");
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AnyAsync(r => r.Customer!.Email == "max@example.test"));
    }

    private static readonly System.Globalization.CultureInfo Dutch = System.Globalization.CultureInfo.GetCultureInfo("nl");

    private sealed record TestTenant(Guid Castles, Guid Pirate, Guid Mill);

    /// <summary>
    /// Een verhuurder met twee categorieën: springkastelen (prinses en piraat) en spellen (molen), en een feestpakket
    /// met de molen.
    /// </summary>
    private async Task<TestTenant> NewTenantAsync(string slug, int bufferDaysAfter = 0, string castlesIntro = "")
    {
        // Elk geval van een theorie gebruikt dezelfde verhuurder.
        if (await TestTenantAsync(slug) is { } existing) return existing;
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = db.Tenants.Single(t => t.Slug == slug);
        tenant.BufferDaysAfter = bufferDaysAfter;
        var castles = new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] };
        var games = new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen"), T("fr", "Jeux"), T("en", "Games")] };
        var princess = new RentalItem
        {
            Slug = "prinses", Category = castles, Stock = 1, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 },
            LengthCm = 500, WidthCm = 400, HeightCm = 350, MinAge = 3, MaxAge = 12, RequiresPower = true,
            Translations = [T("nl", "Prinsessenkasteel")],
        };
        var pirate = new RentalItem { Slug = "piraat", Category = castles, Stock = 1, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Piratenkasteel")] };
        var mill = new RentalItem { Slug = "molen", Category = games, Stock = 1, Pricing = new Pricing { DayPrice = 30 }, Translations = [T("nl", "Molen")] };
        db.AddRange(castles, games, princess, pirate, mill);
        await db.SaveChangesAsync();
        db.Bundles.Add(new Bundle { Slug = "feest", Pricing = new Pricing { DayPrice = 150 }, Translations = [T("nl", "Feestpakket")], Items = [new BundleItem { RentalItemId = mill.Id }] });
        await db.SaveChangesAsync();
        if (castlesIntro.Length > 0)
        {
            var user = await app.CreateUserAsync(slug, $"beheerder@{slug}.test");
            await scope.ServiceProvider.GetRequiredService<IWebsiteTextAdministration>()
                .SaveCategoryAsync(castles.Id, "nl", "Springkastelen", "springkastelen", castlesIntro, user.Id);
        }
        return new TestTenant(castles.Id, pirate.Id, mill.Id);
    }

    private async Task<TestTenant?> TestTenantAsync(string slug)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return null;
        var items = await db.RentalItems.IgnoreQueryFilters().Where(i => i.TenantId == tenant.Id).ToListAsync();
        return new TestTenant(items.Single(i => i.Slug == "piraat").CategoryId!.Value, items.Single(i => i.Slug == "piraat").Id, items.Single(i => i.Slug == "molen").Id);
    }

    private async Task ReserveAsync(string tenantSlug, Guid itemId, DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day),
            [new ReservationLineRequest(itemId, null, 1)],
            new ReservationCustomer("Test", "Klant", "klant@example.test", null, new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
        Assert.True(result.Succeeded);
    }

    private async Task BlockAsync(string tenantSlug, Guid itemId, DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Blockouts.Add(new Blockout { RentalItemId = itemId, StartDate = day, EndDate = day });
        await db.SaveChangesAsync();
    }

    private async Task<string> PageAsync(string tenantSlug, string path) =>
        WebUtility.HtmlDecode(await app.CreateClient($"{tenantSlug}.test").GetStringAsync(path));

    private static int Count(string html, string text) => (html.Length - html.Replace(text, "").Length) / text.Length;

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
