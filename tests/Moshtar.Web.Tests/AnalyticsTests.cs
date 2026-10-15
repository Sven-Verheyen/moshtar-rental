using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Prices;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Google Analytics per verhuurder, met een toestemmingsbalk: zonder meet-ID niets, met een meet-ID pas het script na
/// "Accepteren". De keuze staat in een functionele cookie en kan via de footer gewijzigd worden. De privacyverklaring
/// noemt Google Analytics alleen als de verhuurder het gebruikt.
/// </summary>
public sealed class AnalyticsTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private const string Script = "googletagmanager.com/gtag/js?id=G-TEST12345";

    [Fact]
    public async Task Without_a_measurement_id_there_is_no_bar_no_script_and_no_cookie_settings()
    {
        await NewTenantAsync("ga-uit", null);
        var client = app.CreateClient("ga-uit.test");

        var home = await client.GetStringAsync("/");
        var privacy = WebUtility.HtmlDecode(await client.GetStringAsync("/privacy"));

        Assert.DoesNotContain("class=\"consent\"", home);
        Assert.DoesNotContain("googletagmanager", home);
        Assert.DoesNotContain("Cookie-instellingen", home);
        Assert.Contains("<a href=\"/privacy\">Privacy</a>", home);
        Assert.Contains("<h1>Privacy en cookies</h1>", privacy);
        Assert.DoesNotContain("Google Analytics", privacy);
    }

    [Fact]
    public async Task With_a_measurement_id_the_bar_asks_first_and_no_script_loads_before_consent()
    {
        await NewTenantAsync("ga-vraag", "G-TEST12345");
        var client = app.CreateClient("ga-vraag.test");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/fr/louer/kasteel?van=2099-01-01"));

        Assert.Contains("<form class=\"consent\" method=\"post\" action=\"/toestemming\"", html);
        Assert.Contains("<input type=\"hidden\" name=\"terug\" value=\"/fr/louer/kasteel?van=2099-01-01\"", html);
        Assert.Contains("<button class=\"button\" type=\"submit\" name=\"keuze\" value=\"ja\">Accepter</button>", html);
        Assert.Contains("<button class=\"button\" type=\"submit\" name=\"keuze\" value=\"nee\">Refuser</button>", html);
        Assert.DoesNotContain("googletagmanager", html);
        Assert.DoesNotContain("gtag(", html);
    }

    [Fact]
    public async Task After_accepting_the_script_loads_and_the_bar_is_gone()
    {
        await NewTenantAsync("ga-ja", "G-TEST12345");
        var client = app.CreateClient("ga-ja.test");

        var response = await ChooseAsync(client, "ja", "/huren/kasteel");
        var html = await client.GetStringAsync("/huren/kasteel");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/huren/kasteel", response.Headers.Location!.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("moshtar-analytics"));
        Assert.StartsWith("moshtar-analytics=ja;", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<script async src=\"https://www.{Script}\"></script>", html);
        Assert.Contains("gtag('config','G-TEST12345');", html);
        Assert.DoesNotContain("class=\"consent\"", html);
        Assert.Contains("href=\"/toestemming/wijzigen?terug=%2Fhuren%2Fkasteel\" rel=\"nofollow\">Cookie-instellingen</a>", html);
    }

    [Fact]
    public async Task After_declining_nothing_loads_and_the_choice_can_be_changed_in_the_footer()
    {
        await NewTenantAsync("ga-nee", "G-TEST12345");
        var client = app.CreateClient("ga-nee.test");

        await ChooseAsync(client, "nee", "/");
        var declined = await client.GetStringAsync("/");
        var change = await client.GetAsync("/toestemming/wijzigen?terug=%2Fen%2Fprivacy");
        var again = await client.GetStringAsync("/en/privacy");

        Assert.DoesNotContain("googletagmanager", declined);
        Assert.DoesNotContain("class=\"consent\"", declined);
        Assert.Equal("/en/privacy", change.Headers.Location!.OriginalString);
        Assert.Contains("class=\"consent\"", again);
        Assert.DoesNotContain("googletagmanager", again);
    }

    [Fact]
    public async Task Another_website_cannot_accept_in_the_name_of_the_visitor()
    {
        await NewTenantAsync("ga-vervalst", "G-TEST12345");
        var client = app.CreateClient("ga-vervalst.test");

        var response = await client.PostAsync("/toestemming", new FormUrlEncodedContent(new Dictionary<string, string> { ["keuze"] = "ja", ["terug"] = "/" }));
        var html = await client.GetStringAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("moshtar-analytics")));
        Assert.Contains("class=\"consent\"", html);
        Assert.DoesNotContain("googletagmanager", html);
    }

    [Theory]
    [InlineData("https://kwaad.test/")]
    [InlineData("//kwaad.test/")]
    [InlineData("/\\kwaad.test/")]
    [InlineData("")]
    public async Task The_choice_only_returns_to_a_page_of_the_website(string returnUrl)
    {
        await NewTenantAsync("ga-terug", "G-TEST12345");

        var response = await ChooseAsync(app.CreateClient("ga-terug.test"), "ja", returnUrl);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task With_consent_checking_availability_and_reserving_are_measured_without_personal_data()
    {
        await NewTenantAsync("ga-meten", "G-TEST12345");
        var client = app.CreateClient("ga-meten.test");
        await ChooseAsync(client, "ja", "/");
        var day = TestReservations.FutureDay(40);
        var path = $"/huren/kasteel?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";

        var form = await client.GetStringAsync(path);
        var confirmed = await WebsiteCart.ReserveItemAsync(client, path, "greta@example.test", "Greta",
            delivery: "Delivery", phone: "+32 470 00 00 00", street: "Meetstraat 9");

        Assert.Contains("<script>gtag('event',\"check_availability\",{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\"," +
            $"\"start_date\":\"{day:yyyy-MM-dd}\",\"end_date\":\"{day.AddDays(1):yyyy-MM-dd}\",\"days\":2,\"available\":true}});</script>", form);
        var purchase = System.Text.RegularExpressions.Regex.Match(confirmed, "<script>gtag\\('event',\"purchase\",(.*?)\\);</script>");
        Assert.True(purchase.Success, "De bevestigde reservatie wordt gemeten.");
        Assert.Matches("^\\{\"transaction_id\":\"[^\"]+\",\"value\":200.00,\"currency\":\"EUR\",\"items\":\\[\\{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\",\"quantity\":1,\"price\":200.00\\}\\]\\}$", purchase.Groups[1].Value);
        foreach (var personal in new[] { "Greta", "Klant", "greta@example", "470 00", "Meetstraat" })
            Assert.DoesNotContain(personal, string.Join("", System.Text.RegularExpressions.Regex.Matches(confirmed, "<script>gtag.*?</script>").Select(m => m.Value)));
    }

    [Fact]
    public async Task With_consent_adding_to_the_cart_viewing_it_and_starting_to_reserve_are_measured_without_personal_data()
    {
        await NewTenantAsync("ga-winkelkar", "G-TEST12345");
        var client = app.CreateClient("ga-winkelkar.test");
        await ChooseAsync(client, "ja", "/");
        var day = TestReservations.FutureDay(42);

        var added = await WebsiteCart.AddAsync(client, $"/huren/kasteel?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}");
        var item = await client.GetStringAsync(added.Headers.Location!.OriginalString);
        var cart = await client.GetStringAsync("/winkelkar");
        var step1 = await WebsiteCart.IdentifyAsync(client, "wim@example.test", news: true);
        var step2 = await client.GetStringAsync(step1.Headers.Location!.OriginalString);

        // 2 dagen kasteel aan 100 per dag.
        const string items = "\"currency\":\"EUR\",\"items\":[{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\",\"quantity\":1,\"price\":200";
        Assert.Contains(items, Event(item, "add_to_cart"));
        Assert.Contains(items, Event(cart, "view_cart"));
        Assert.Contains(items, Event(step2, "begin_checkout"));
        Assert.Null(Event(step2, "view_cart"));
        Assert.DoesNotContain("wim@", Event(step2, "begin_checkout"));
    }

    [Fact]
    public async Task Cart_events_measure_each_line_at_its_price_after_the_discounts_it_got()
    {
        await NewTenantAsync("ga-netto", "G-TEST12345");
        await AddGameAndComboAsync("ga-netto");
        var client = app.CreateClient("ga-netto.test");
        await ChooseAsync(client, "ja", "/");
        var day = TestReservations.FutureDay(44);
        var period = $"?van={day:yyyy-MM-dd}";

        await WebsiteCart.AddAsync(client, "/huren/kasteel" + period);
        await WebsiteCart.AddAsync(client, "/huren/spel" + period);
        var cart = await client.GetStringAsync("/winkelkar");
        await WebsiteCart.IdentifyAsync(client, "nina@example.test");
        var reserved = await WebsiteCart.ReserveAsync(client, "Nina");
        var confirmed = WebUtility.HtmlDecode(await reserved.Content.ReadAsStringAsync());

        // Kasteel 100 en spel 50: het spel is het goedkoopste stuk en krijgt 40% = 20 korting. De kortingslijn is geen item.
        Assert.Equal("{\"value\":130.00,\"currency\":\"EUR\",\"items\":[" +
            "{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\",\"quantity\":1,\"price\":100.00}," +
            "{\"item_id\":\"spel\",\"item_name\":\"Spel\",\"quantity\":1,\"price\":30.00}]}", Event(cart, "view_cart"));
        // De reservatie bewaart alleen de kortingslijn: verdeeld over de lijnen naar hun bedrag (20 × 100/150 = 13,33 en
        // 20 × 50/150 = 6,67), een benadering voor GA4.
        Assert.Matches("^\\{\"transaction_id\":\"[^\"]+\",\"value\":130.00,\"currency\":\"EUR\",\"items\":\\[" +
            "\\{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\",\"quantity\":1,\"price\":86.67\\}," +
            "\\{\"item_id\":\"spel\",\"item_name\":\"Spel\",\"quantity\":1,\"price\":43.33\\}\\]\\}$", Event(confirmed, "purchase"));
    }

    [Fact]
    public async Task Without_consent_the_cart_is_not_measured()
    {
        await NewTenantAsync("ga-winkelkar-nee", "G-TEST12345");
        var client = app.CreateClient("ga-winkelkar-nee.test");
        var day = TestReservations.FutureDay(43);

        var added = await WebsiteCart.AddAsync(client, $"/huren/kasteel?van={day:yyyy-MM-dd}");
        var item = await client.GetStringAsync(added.Headers.Location!.OriginalString);
        var cart = await client.GetStringAsync("/winkelkar");

        Assert.DoesNotContain("gtag(", item);
        Assert.DoesNotContain("gtag(", cart);
    }

    /// <summary>De gegevens van een GA4-gebeurtenis op de pagina, of null als ze er niet is.</summary>
    private static string? Event(string html, string name) =>
        System.Text.RegularExpressions.Regex.Match(html, $"<script>gtag\\('event',\"{name}\",(.*?)\\);</script>") is { Success: true } m
            ? m.Groups[1].Value
            : null;

    [Fact]
    public async Task Without_consent_no_events_are_measured()
    {
        await NewTenantAsync("ga-geen-meting", "G-TEST12345");
        var day = TestReservations.FutureDay(41);

        var html = await app.CreateClient("ga-geen-meting.test").GetStringAsync($"/huren/kasteel?van={day:yyyy-MM-dd}");

        Assert.DoesNotContain("gtag(", html);
    }

    [Theory]
    [InlineData("/privacy", "Privacy en cookies", "Je reservatie")]
    [InlineData("/fr/confidentialite", "Confidentialité et cookies", "Votre réservation")]
    [InlineData("/en/privacy", "Privacy and cookies", "Your reservation")]
    public async Task The_privacy_statement_is_in_every_language_with_the_details_of_the_rental_company(string path, string title, string reservation)
    {
        await NewTenantAsync("ga-privacy", "G-TEST12345");
        await using (var scope = await app.TenantScopeAsync("ga-privacy"))
            await scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>().SaveAsync(new BusinessDetailsSettings
            {
                Email = "info@privacy.test", Street = "Privacystraat 1", PostalCode = "2640", City = "Mortsel",
                VatNumber = "BE 0123.456.789", GoogleAnalyticsId = "G-TEST12345",
            });

        var response = await app.CreateClient("ga-privacy.test").GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<h1>{title}</h1>", html);
        Assert.Contains($"<h2>{reservation}</h2>", html);
        var main = html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];
        foreach (var detail in new[] { "ga-privacy", "Privacystraat 1", "2640 Mortsel", "info@privacy.test", "BE 0123.456.789" })
            Assert.Contains(detail, main);
        Assert.Contains("<h2 id=\"google-analytics\">Google Analytics</h2>", main);
    }

    [Fact]
    public async Task The_privacy_page_has_the_word_of_its_language()
    {
        await NewTenantAsync("ga-woord", null);

        var response = await app.CreateClient("ga-woord.test").GetAsync("/fr/privacy");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/fr/confidentialite", response.Headers.Location!.OriginalString);
    }

    /// <summary>Zoals een browser: de balk ophalen en het formulier met zijn antiforgery-token versturen.</summary>
    private static async Task<HttpResponseMessage> ChooseAsync(HttpClient client, string choice, string returnUrl)
    {
        var page = await client.GetStringAsync("/");
        return await client.PostAsync("/toestemming", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = BackOffice.Field(page, "__RequestVerificationToken"),
            ["keuze"] = choice,
            ["terug"] = returnUrl,
        }));
    }

    /// <summary>Een spel van 50 per dag, en "2e stuk 40% korting" op het kasteel en het spel samen.</summary>
    private async Task AddGameAndComboAsync(string slug)
    {
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = new RentalItem
        {
            Slug = "spel", Stock = 1, Pricing = new Pricing { DayPrice = 50 },
            Translations = [new Translation { Culture = "nl", Name = "Spel" }],
        };
        db.RentalItems.Add(game);
        await db.SaveChangesAsync();
        var castle = db.RentalItems.Single(i => i.Slug == "kasteel");
        await scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>().CreateComboAsync(new ComboDiscountInput(
            [new("nl", "2e stuk 40% korting")], PricingTarget.Chosen([castle.Id, game.Id], []), 2, 40));
    }

    private async Task NewTenantAsync(string slug, string? measurementId)
    {
        // Een theorie gebruikt dezelfde verhuurder meermaals.
        await using (var root = app.Services.CreateAsyncScope())
            if (root.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.Any(t => t.Slug == slug)) return;
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RentalItems.Add(new RentalItem
        {
            Slug = "kasteel", Stock = 1, Pricing = new Pricing { DayPrice = 100 },
            Translations = new[] { "nl", "fr", "en" }.Select(c => new Translation { Culture = c, Name = "Kasteel" }).ToList(),
        });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>()
            .SaveAsync(new BusinessDetailsSettings { Email = $"info@{slug}.test", GoogleAnalyticsId = measurementId });
    }
}
