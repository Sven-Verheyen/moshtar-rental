using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Matches("^\\{\"transaction_id\":\"[^\"]+\",\"value\":200.00,\"currency\":\"EUR\",\"items\":\\[\\{\"item_id\":\"kasteel\",\"item_name\":\"Kasteel\",\"quantity\":1\\}\\]\\}$", purchase.Groups[1].Value);
        foreach (var personal in new[] { "Greta", "Klant", "greta@example", "470 00", "Meetstraat" })
            Assert.DoesNotContain(personal, string.Join("", System.Text.RegularExpressions.Regex.Matches(confirmed, "<script>gtag.*?</script>").Select(m => m.Value)));
    }

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
