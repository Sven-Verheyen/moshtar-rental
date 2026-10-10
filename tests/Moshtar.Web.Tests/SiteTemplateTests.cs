using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Het sjabloon van de reservatiewebsite: ingekleurd met de huisstijl van de verhuurder, met de sfeerfoto bovenaan de home
/// en overal de knop "Kies je datum". De publieke pagina's laden geen MudBlazor; het back office wel.
/// </summary>
public sealed class SiteTemplateTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task The_website_uses_the_logo_colour_and_hero_image_of_the_rental_company()
    {
        await app.CreateTenantAsync("sjabloon-merk", "sjabloon-merk.test");
        await using (var scope = await app.TenantScopeAsync("sjabloon-merk"))
            await scope.ServiceProvider.GetRequiredService<IBrandingAdministration>()
                .SaveAsync(new BrandingSettings("https://merk.test/logo.png", "#ffeb3b", "https://merk.test/feest.jpg"));

        var html = await app.CreateClient("sjabloon-merk.test").GetStringAsync("/");

        Assert.Contains("<img src=\"https://merk.test/logo.png\" alt=\"sjabloon-merk\"", html);
        Assert.Contains("--brand: #ffeb3b; --on-brand: #000000;", html);
        Assert.Contains("<img class=\"hero-image\" src=\"https://merk.test/feest.jpg\"", html);
        Assert.DoesNotContain("hero-plain", html);
    }

    [Fact]
    public async Task Without_branding_the_website_has_a_neutral_colour_and_a_plain_hero()
    {
        await app.CreateTenantAsync("sjabloon-neutraal", "sjabloon-neutraal.test");

        var html = await app.CreateClient("sjabloon-neutraal.test").GetStringAsync("/");

        Assert.Contains("--brand: #1f2933; --on-brand: #ffffff;", html);
        Assert.Contains("class=\"hero hero-plain\"", html);
        Assert.DoesNotContain("hero-image", html);
        Assert.Matches(@"<a class=""brand"" href=""/"">\s*sjabloon-neutraal\s*</a>", html);
    }

    [Theory]
    [InlineData("/", "Kies je datum", "/#aanbod")]
    [InlineData("/fr", "Choisissez votre date", "/fr#aanbod")]
    [InlineData("/en/rent/sjabloon-kasteel", "Pick your date", "#reserveren")]
    public async Task Every_page_has_the_choose_your_date_button(string path, string label, string target)
    {
        await EnsureItemAsync();

        var html = WebUtility.HtmlDecode(await app.CreateClient(MoshtarApp.HopsakeeHost).GetStringAsync(path));

        Assert.Contains($"<a class=\"button cta\" href=\"{target}\">{label}</a>", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/huren/sjabloon-kasteel")]
    [InlineData("/bestaat-niet")]
    public async Task Public_pages_load_no_mudblazor_and_no_javascript(string path)
    {
        await EnsureItemAsync();

        var html = await (await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync(path)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("MudBlazor", html);
        Assert.DoesNotContain("<script", html);
        // Zonder <base> wijzen ankers als #reserveren naar de pagina zelf.
        Assert.DoesNotContain("<base", html);
        Assert.Matches(@"<link rel=""stylesheet"" href=""/site\.[a-z0-9]+\.css""", html);
    }

    [Fact]
    public async Task The_back_office_still_loads_mudblazor()
    {
        var client = await app.LoggedInClientAsync("sjabloon-beheerder@hopsakee.test", UserRole.Administrator);

        var html = await client.GetStringAsync("/admin/huisstijl");

        Assert.Contains("MudBlazor.min", html);
        Assert.Contains("blazor.web", html);
    }

    [Fact]
    public async Task An_unknown_back_office_page_is_not_found()
    {
        var client = await app.LoggedInClientAsync("sjabloon-onbekend@hopsakee.test", UserRole.Administrator);

        var response = await client.GetAsync("/admin/bestaat-niet");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Kasteel", "K")]
    [InlineData("éénhoorn", "É")]
    [InlineData("🎈 Ballon", "🎈")]
    [InlineData("", "")]
    public void An_item_without_a_photo_shows_its_first_letter(string name, string initial) =>
        Assert.Equal(initial, Moshtar.Web.Site.SiteText.Initial(name));

    [Fact]
    public void A_phone_link_keeps_only_plus_and_digits() =>
        Assert.Equal("+32475123456", Moshtar.Web.Site.SiteText.PhoneLink("+32 475/12.34.56"));

    private async Task EnsureItemAsync()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.RentalItems.AnyAsync(i => i.Slug == "sjabloon-kasteel")) return;
        db.RentalItems.Add(new RentalItem
        {
            Slug = "sjabloon-kasteel", Stock = 1, Pricing = new Pricing { DayPrice = 100 },
            Translations = new[] { "nl", "fr", "en" }.Select(c => new Translation { Culture = c, Name = "Kasteel" }).ToList(),
        });
        await db.SaveChangesAsync();
    }
}
