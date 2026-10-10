using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Identity;

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
        Assert.Contains("href=\"admin/bedrijfsgegevens\"", await page.Content.ReadAsStringAsync());
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

    private static IBusinessDetailsAdministration Admin(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
}
