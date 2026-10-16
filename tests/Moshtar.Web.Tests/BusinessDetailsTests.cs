using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder vult de bedrijfsgegevens en het werkgebied van zijn verhuurder in. Ze staan in de footer van de
/// reservatiewebsite en op de contactpagina, enkel wat ingevuld is.
/// </summary>
public sealed class BusinessDetailsTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly BusinessDetailsSettings Full = new()
    {
        Street = " Kerkstraat 1 ", PostalCode = "2640", City = "Mortsel",
        Email = "info@feest.test", Phone = "+32 475 12 34 56", WhatsAppPhone = "+32 476 00 11 22", VatNumber = "BE 0123.456.789",
        FacebookUrl = "https://www.facebook.com/feest", InstagramUrl = "https://www.instagram.com/feest", TikTokUrl = "https://www.tiktok.com/@feest",
        GoogleBusinessUrl = "https://g.page/feest",
        ServiceArea = ["Mortsel", " Edegem ", "", "mortsel", "Kontich"],
        OffersPickup = true,
    };

    [Fact]
    public async Task Business_details_are_saved_cleaned_and_cleared()
    {
        await app.CreateTenantAsync("gegevens-bewaren", "gegevens-bewaren.test");
        await using var scope = await app.TenantScopeAsync("gegevens-bewaren");
        var admin = Admin(scope);

        await admin.SaveAsync(Full);
        var saved = await admin.GetAsync();
        Assert.Equal("Kerkstraat 1", saved.Street);
        Assert.Equal("+32 475 12 34 56", saved.Phone);
        Assert.Equal("https://www.tiktok.com/@feest", saved.TikTokUrl);
        Assert.Equal(["Mortsel", "Edegem", "Kontich"], saved.ServiceArea);
        Assert.True(saved.OffersPickup);

        await admin.SaveAsync(new BusinessDetailsSettings { Email = "info@feest.test", Street = " " });
        Assert.Equivalent(new BusinessDetailsSettings { Email = "info@feest.test" }, await admin.GetAsync(), strict: true);
    }

    [Fact]
    public async Task Invalid_details_are_refused_and_not_saved()
    {
        await app.CreateTenantAsync("gegevens-ongeldig", "gegevens-ongeldig.test");
        await using var scope = await app.TenantScopeAsync("gegevens-ongeldig");
        var admin = Admin(scope);
        await admin.SaveAsync(Full);

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(() => admin.SaveAsync(Full with
        {
            Phone = "0475 12 34 56", FacebookUrl = "http://www.facebook.com/feest", Street = "Andere straat 2",
        }));

        Assert.Equal(2, refused.Problems.Count);
        Assert.Equal("Kerkstraat 1", (await admin.GetAsync()).Street);
    }

    [Fact]
    public async Task A_value_longer_than_allowed_is_refused_instead_of_failing_in_the_database()
    {
        await app.CreateTenantAsync("gegevens-lang", "gegevens-lang.test");
        await using var scope = await app.TenantScopeAsync("gegevens-lang");

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(() =>
            Admin(scope).SaveAsync(Full with { Street = new string('a', 201), FacebookUrl = "https://www.facebook.com/" + new string('a', 480) }));

        Assert.Equal(["De straat mag hoogstens 200 tekens lang zijn.", "De link naar je Facebook mag hoogstens 500 tekens lang zijn."], refused.Problems);
    }

    [Fact]
    public async Task The_google_analytics_measurement_id_is_optional_cleaned_and_validated()
    {
        await app.CreateTenantAsync("gegevens-analytics", "gegevens-analytics.test");
        await using var scope = await app.TenantScopeAsync("gegevens-analytics");
        var admin = Admin(scope);

        await admin.SaveAsync(new BusinessDetailsSettings { Email = "info@feest.test", GoogleAnalyticsId = " g-abc123xyz " });
        Assert.Equal("G-ABC123XYZ", (await admin.GetAsync()).GoogleAnalyticsId);

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(() =>
            admin.SaveAsync(new BusinessDetailsSettings { Email = "info@feest.test", GoogleAnalyticsId = "UA-12345-1" }));
        Assert.Equal(["Het meet-ID van Google Analytics begint met G- gevolgd door letters en cijfers, bv. G-ABC123XYZ."], refused.Problems);
        Assert.Equal("G-ABC123XYZ", (await admin.GetAsync()).GoogleAnalyticsId);

        await admin.SaveAsync(new BusinessDetailsSettings { Email = "info@feest.test", GoogleAnalyticsId = " " });
        Assert.Null((await admin.GetAsync()).GoogleAnalyticsId);
    }

    [Fact]
    public async Task The_round_settings_start_from_the_standard_and_can_be_changed()
    {
        await app.CreateTenantAsync("gegevens-rondes", "gegevens-rondes.test");
        await using var scope = await app.TenantScopeAsync("gegevens-rondes");
        var admin = Admin(scope);
        Assert.Equal(new RoundSettings(new TimeOnly(8, 0), new TimeOnly(18, 0), 30, 20, 60), (await admin.GetAsync()).Rounds);

        var changed = new RoundSettings(new TimeOnly(7, 30), new TimeOnly(17, 0), 45, 25, 120);
        await admin.SaveAsync(await admin.GetAsync() with { Rounds = changed });

        Assert.Equal(changed, (await admin.GetAsync()).Rounds);
    }

    [Fact]
    public async Task Round_settings_that_make_no_sense_are_refused()
    {
        await app.CreateTenantAsync("gegevens-rondes-fout", "gegevens-rondes-fout.test");
        await using var scope = await app.TenantScopeAsync("gegevens-rondes-fout");
        var admin = Admin(scope);

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(async () => await admin.SaveAsync(await admin.GetAsync() with
        {
            Rounds = new RoundSettings(new TimeOnly(8, 0), new TimeOnly(18, 0), -5, 20, 45),
        }));

        Assert.Equal([
            "De opbouwtijd moet tussen 0 en 480 minuten liggen.",
            "De breedte van het venster is een veelvoud van 30 minuten, van 30 tot 240 minuten.",
        ], refused.Problems);
        Assert.Equal(30, (await admin.GetAsync()).Rounds.SetupMinutes);
    }

    [Fact]
    public async Task The_contact_page_shows_a_postal_code_without_a_street()
    {
        await app.CreateTenantAsync("gegevens-postcode", "gegevens-postcode.test");
        await using (var scope = await app.TenantScopeAsync("gegevens-postcode"))
            await Admin(scope).SaveAsync(new BusinessDetailsSettings { Email = "info@feest.test", PostalCode = "2640" });

        var html = await app.CreateClient("gegevens-postcode.test").GetStringAsync("/contact");

        Assert.Contains("<span>2640</span>", html[..html.IndexOf("<footer", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task The_email_address_cannot_be_cleared_because_mails_need_it()
    {
        await app.CreateTenantAsync("gegevens-mail", "gegevens-mail.test");
        await using var scope = await app.TenantScopeAsync("gegevens-mail");

        await Assert.ThrowsAsync<BusinessDetailsException>(() => Admin(scope).SaveAsync(new BusinessDetailsSettings()));

        Assert.Equal("info@gegevens-mail.test", (await Admin(scope).GetAsync()).Email);
    }

    [Fact]
    public async Task The_footer_shows_only_what_is_filled_in()
    {
        await app.CreateTenantAsync("gegevens-footer", "gegevens-footer.test");
        await using (var scope = await app.TenantScopeAsync("gegevens-footer"))
            await Admin(scope).SaveAsync(Full with { InstagramUrl = null, WhatsAppPhone = null });

        var html = WebUtility.HtmlDecode(await app.CreateClient("gegevens-footer.test").GetStringAsync("/"));
        var footer = html[html.IndexOf("<footer", StringComparison.Ordinal)..];

        Assert.Contains("<span>Kerkstraat 1</span>", footer);
        Assert.Contains("<span>2640 Mortsel</span>", footer);
        Assert.Contains("<a href=\"mailto:info@feest.test\">info@feest.test</a>", footer);
        Assert.Contains("<a href=\"tel:+32475123456\">+32 475 12 34 56</a>", footer);
        Assert.Contains("<span>Btw BE 0123.456.789</span>", footer);
        Assert.Contains("href=\"https://www.facebook.com/feest\"", footer);
        Assert.Contains("href=\"https://www.tiktok.com/@feest\"", footer);
        Assert.DoesNotContain("Instagram", footer);
        Assert.Contains("<a href=\"https://g.page/feest\" rel=\"noopener\" target=\"_blank\">Lees onze reviews op Google</a>", footer);
    }

    [Fact]
    public async Task Without_details_the_footer_shows_just_the_name_and_email()
    {
        await app.CreateTenantAsync("gegevens-leeg", "gegevens-leeg.test");

        var html = await app.CreateClient("gegevens-leeg.test").GetStringAsync("/");
        var footer = html[html.IndexOf("<footer", StringComparison.Ordinal)..];

        Assert.Contains("mailto:info@gegevens-leeg.test", footer);
        Assert.DoesNotContain("tel:", footer);
        Assert.DoesNotContain("Btw", footer);
        Assert.DoesNotContain("class=\"social\"", footer);
        Assert.DoesNotContain("Google", footer);
    }

    [Theory]
    [InlineData("/contact", "nl", "Bellen", "Hier leveren we")]
    [InlineData("/fr/contact", "fr", "Appeler", "Nous livrons à")]
    [InlineData("/en/contact", "en", "Call", "Where we deliver")]
    public async Task The_contact_page_has_buttons_to_call_mail_and_whatsapp_and_the_service_area(
        string path, string culture, string call, string serviceArea)
    {
        await app.CreateTenantAsync("gegevens-contact-" + culture, $"gegevens-contact-{culture}.test");
        await using (var scope = await app.TenantScopeAsync("gegevens-contact-" + culture))
            await Admin(scope).SaveAsync(Full);

        var response = await app.CreateClient($"gegevens-contact-{culture}.test").GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\">", html);
        Assert.Contains($"<a class=\"button\" href=\"tel:+32475123456\">{call} <span class=\"button-detail\">+32 475 12 34 56</span></a>", html);
        Assert.Contains("<a class=\"button\" href=\"mailto:info@feest.test\">", html);
        Assert.Contains("<a class=\"button\" href=\"https://wa.me/32476001122\"", html);
        Assert.Contains($"<h2 id=\"werkgebied\">{serviceArea}</h2>", html);
        Assert.Contains("<li>Edegem</li>", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://gegevens-contact-{culture}.test{path}\" />", html);
        Assert.DoesNotContain("<form", html);
    }

    [Fact]
    public async Task The_menu_links_to_the_contact_page_in_the_language_of_the_page()
    {
        var html = await app.CreateClient(MoshtarApp.HopsakeeHost).GetStringAsync("/fr");

        Assert.Contains("<a href=\"/fr/contact\">Contact</a>", html);
    }

    [Fact]
    public async Task One_rental_company_never_shows_the_details_of_another()
    {
        await app.CreateTenantAsync("gegevens-een", "gegevens-een.test");
        await app.CreateTenantAsync("gegevens-twee", "gegevens-twee.test");
        await using (var one = await app.TenantScopeAsync("gegevens-een"))
            await Admin(one).SaveAsync(Full);

        await using (var two = await app.TenantScopeAsync("gegevens-twee"))
            Assert.Null((await Admin(two).GetAsync()).Street);
        var client = app.CreateClient("gegevens-twee.test");
        foreach (var path in new[] { "/", "/contact" })
        {
            var html = await client.GetStringAsync(path);
            Assert.DoesNotContain("Kerkstraat", html);
            Assert.DoesNotContain("Edegem", html);
            Assert.DoesNotContain("info@feest.test", html);
        }
    }

    [Fact]
    public async Task Administrator_finds_the_business_details_in_the_menu()
    {
        var client = await app.LoggedInClientAsync("gegevens-menu@hopsakee.test", UserRole.Administrator);

        var page = await client.GetAsync("/admin/bedrijfsgegevens");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("href=\"admin/bedrijfsgegevens\"", html);
        Assert.Contains("Vertrekuur leverronde", html);
        Assert.Contains("Breedte venster", html);
    }

    [Fact]
    public async Task Administrator_tests_the_review_link_and_downloads_its_qr_code_in_the_business_details()
    {
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var admin = Admin(scope);
            await admin.SaveAsync(await admin.GetAsync() with { ReviewLink = "https://g.page/r/hopsakee/review" });
        }
        var client = await app.LoggedInClientAsync("gegevens-review@hopsakee.test", UserRole.Administrator);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/bedrijfsgegevens"));

        Assert.Contains("Google-reviewlink", html);
        Assert.Contains("Reviewvragen sturen", html);
        Assert.Matches("<a(?=[^>]*href=\"https://g.page/r/hopsakee/review\")(?=[^>]*target=\"_blank\")[^>]*>(?:(?!</a>).)*Link testen", html);
        Assert.Matches("<a(?=[^>]*href=\"/review-qr.png[^\"]*\")(?=[^>]*download)[^>]*>(?:(?!</a>).)*QR-code downloaden", html);
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_business_details()
    {
        var client = await app.LoggedInClientAsync("gegevens-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/bedrijfsgegevens");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/bedrijfsgegevens\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Review_requests_are_off_by_default()
    {
        await app.CreateTenantAsync("review-standaard", "review-standaard.test");
        await using var scope = await app.TenantScopeAsync("review-standaard");

        var saved = await Admin(scope).GetAsync();

        Assert.Null(saved.ReviewLink);
        Assert.False(saved.SendReviewRequests);
        Assert.Null(saved.ReviewRequestsSince);
    }

    [Fact]
    public async Task The_review_link_is_saved_and_must_be_a_full_https_address()
    {
        await app.CreateTenantAsync("review-link", "review-link.test");
        await using var scope = await app.TenantScopeAsync("review-link");
        var admin = Admin(scope);

        await admin.SaveAsync(Full with { ReviewLink = " https://g.page/r/feest/review " });
        Assert.Equal("https://g.page/r/feest/review", (await admin.GetAsync()).ReviewLink);

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(() => admin.SaveAsync(Full with { ReviewLink = "g.page/r/feest/review" }));
        Assert.Equal(["De Google-reviewlink moet een volledig adres zijn dat begint met https://, zoals Google het geeft bij \"Vraag om reviews\"."],
            refused.Problems);
        Assert.Equal("https://g.page/r/feest/review", (await admin.GetAsync()).ReviewLink);
    }

    [Fact]
    public async Task Review_requests_cannot_be_turned_on_without_a_review_link()
    {
        await app.CreateTenantAsync("review-zonder-link", "review-zonder-link.test");
        await using var scope = await app.TenantScopeAsync("review-zonder-link");
        var admin = Admin(scope);

        var refused = await Assert.ThrowsAsync<BusinessDetailsException>(() => admin.SaveAsync(Full with { SendReviewRequests = true }));

        Assert.Equal(["Vul eerst je Google-reviewlink in: zonder link kunnen er geen reviewvragen vertrekken."], refused.Problems);
        Assert.False((await admin.GetAsync()).SendReviewRequests);
    }

    [Fact]
    public async Task The_moment_review_requests_are_turned_on_is_kept_until_they_are_turned_off()
    {
        await app.CreateTenantAsync("review-aan", "review-aan.test");
        var monday = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);
        var tuesday = monday.AddDays(1);
        var withLink = Full with { ReviewLink = "https://g.page/r/feest/review" };

        await SaveAtAsync("review-aan", monday, withLink with { SendReviewRequests = true });
        var on = await SaveAtAsync("review-aan", tuesday, withLink with { SendReviewRequests = true, Phone = "+32 475 99 99 99" });
        Assert.True(on.SendReviewRequests);
        Assert.Equal(monday, on.ReviewRequestsSince);

        var off = await SaveAtAsync("review-aan", tuesday, withLink);
        Assert.False(off.SendReviewRequests);
        Assert.Null(off.ReviewRequestsSince);

        var onAgain = await SaveAtAsync("review-aan", tuesday, withLink with { SendReviewRequests = true });
        Assert.Equal(tuesday, onAgain.ReviewRequestsSince);
    }

    [Fact]
    public async Task Without_a_review_link_there_is_no_qr_code()
    {
        await app.CreateTenantAsync("review-qr-leeg", "review-qr-leeg.test");

        var response = await app.CreateClient("review-qr-leeg.test").GetAsync("/review-qr.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_qr_code_of_the_review_link_is_a_public_png_that_changes_with_the_link()
    {
        await app.CreateTenantAsync("review-qr", "review-qr.test");
        var client = app.CreateClient("review-qr.test");

        await using (var scope = await app.TenantScopeAsync("review-qr"))
            await Admin(scope).SaveAsync(Full with { ReviewLink = "https://g.page/r/feest/review" });
        var first = await client.GetAsync("/review-qr.png");
        await using (var scope = await app.TenantScopeAsync("review-qr"))
            await Admin(scope).SaveAsync(Full with { ReviewLink = "https://g.page/r/ander-feest/review" });
        var second = await client.GetAsync("/review-qr.png");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        var png = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        Assert.NotEqual(png, await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_qr_code_belongs_to_the_rental_company_of_the_domain()
    {
        await app.CreateTenantAsync("review-qr-een", "review-qr-een.test");
        await app.CreateTenantAsync("review-qr-twee", "review-qr-twee.test");
        await using (var scope = await app.TenantScopeAsync("review-qr-een"))
            await Admin(scope).SaveAsync(Full with { ReviewLink = "https://g.page/r/feest/review" });

        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient("review-qr-een.test").GetAsync("/review-qr.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient("review-qr-twee.test").GetAsync("/review-qr.png")).StatusCode);
    }

    /// <summary>Bewaart op dit tijdstip en geeft terug wat er daarna bewaard staat.</summary>
    private async Task<BusinessDetailsSettings> SaveAtAsync(string slug, DateTimeOffset now, BusinessDetailsSettings settings)
    {
        await using var at = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        await using var scope = at.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Hosts).SingleAsync(t => t.Slug == slug);
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
        await Admin(scope).SaveAsync(settings);
        return await Admin(scope).GetAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static IBusinessDetailsAdministration Admin(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
}
