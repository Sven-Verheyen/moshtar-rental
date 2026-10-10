using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Tenancy;

namespace Moshtar.Web.Tests;

/// <summary>Een beheerder stelt het logo en de kleur van zijn verhuurder in, voor de kop van de klantmails.</summary>
public sealed class BrandingAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Logo_and_colour_are_saved_changed_and_cleared()
    {
        await app.CreateTenantAsync("huisstijl-bewaren", "huisstijl-bewaren.test");
        await using var scope = await app.TenantScopeAsync("huisstijl-bewaren");
        var branding = Branding(scope);

        await branding.SaveAsync(new BrandingSettings(" https://merk.test/logo.png ", "#E91E63"));
        Assert.Equal(new BrandingSettings("https://merk.test/logo.png", "#e91e63"), await branding.GetAsync());

        await branding.SaveAsync(new BrandingSettings("https://merk.test/nieuw.png", "#f60"));
        Assert.Equal(new BrandingSettings("https://merk.test/nieuw.png", "#f60"), await branding.GetAsync());

        await branding.SaveAsync(new BrandingSettings("", " "));
        Assert.Equal(new BrandingSettings(null, null), await branding.GetAsync());
    }

    [Fact]
    public async Task The_hero_image_is_saved_and_cleared_like_the_logo()
    {
        await app.CreateTenantAsync("huisstijl-sfeerfoto", "huisstijl-sfeerfoto.test");
        await using var scope = await app.TenantScopeAsync("huisstijl-sfeerfoto");
        var branding = Branding(scope);

        await branding.SaveAsync(new BrandingSettings(null, null, " https://merk.test/feest.jpg "));
        Assert.Equal(new BrandingSettings(null, null, "https://merk.test/feest.jpg"), await branding.GetAsync());

        var refused = await Assert.ThrowsAsync<BrandingException>(() => branding.SaveAsync(new BrandingSettings(null, null, "http://merk.test/feest.jpg")));
        Assert.Equal(["De sfeerfoto moet een volledig adres zijn dat begint met https://."], refused.Problems);

        await branding.SaveAsync(new BrandingSettings(null, null, ""));
        Assert.Equal(new BrandingSettings(null, null), await branding.GetAsync());
    }

    [Fact]
    public async Task An_invalid_logo_or_colour_is_refused_and_not_saved()
    {
        await app.CreateTenantAsync("huisstijl-ongeldig", "huisstijl-ongeldig.test");
        await using var scope = await app.TenantScopeAsync("huisstijl-ongeldig");
        var branding = Branding(scope);
        await branding.SaveAsync(new BrandingSettings("https://merk.test/logo.png", "#123456"));

        var refused = await Assert.ThrowsAsync<BrandingException>(() =>
            branding.SaveAsync(new BrandingSettings("http://merk.test/logo.png", "red;background:url(x)")));

        Assert.Equal(2, refused.Problems.Count);
        Assert.Equal(new BrandingSettings("https://merk.test/logo.png", "#123456"), await branding.GetAsync());
    }

    [Fact]
    public async Task One_rental_company_never_changes_the_branding_of_another()
    {
        await app.CreateTenantAsync("huisstijl-een", "huisstijl-een.test");
        await app.CreateTenantAsync("huisstijl-twee", "huisstijl-twee.test");
        await using (var one = await app.TenantScopeAsync("huisstijl-een"))
            await Branding(one).SaveAsync(new BrandingSettings("https://een.test/logo.png", "#111111"));

        await using var two = await app.TenantScopeAsync("huisstijl-twee");
        Assert.Equal(new BrandingSettings(null, null), await Branding(two).GetAsync());
    }

    [Fact]
    public async Task The_next_mail_uses_the_new_branding_right_away()
    {
        await app.CreateTenantAsync("huisstijl-meteen", "huisstijl-meteen.test");
        var store = app.Services.GetRequiredService<ITenantStore>();
        Assert.Null(store.FindByHost("huisstijl-meteen.test")!.PrimaryColor);

        await using (var scope = await app.TenantScopeAsync("huisstijl-meteen"))
            await Branding(scope).SaveAsync(new BrandingSettings(null, "#e91e63"));

        Assert.Equal("#e91e63", store.FindByHost("huisstijl-meteen.test")!.PrimaryColor);
    }

    [Fact]
    public async Task Mails_from_the_same_screen_use_the_new_branding_right_away()
    {
        await app.CreateTenantAsync("huisstijl-scherm", "huisstijl-scherm.test");
        await using var scope = await app.TenantScopeAsync("huisstijl-scherm");

        await Branding(scope).SaveAsync(new BrandingSettings("https://merk.test/logo.png", "#e91e63"));

        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant!;
        Assert.Equal(("https://merk.test/logo.png", "#e91e63"), (tenant.LogoUrl, tenant.PrimaryColor));
    }

    [Fact]
    public async Task The_preview_shows_the_header_as_in_the_mail()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var branding = Branding(scope);

        Assert.StartsWith("<div style=\"background-color:#f4f4f5;", branding.PreviewHeader(new BrandingSettings("https://merk.test/logo.png", null)));
        Assert.Contains(">Hopsakee.fun</span>", branding.PreviewHeader(new BrandingSettings(null, "#e91e63")));
        Assert.Equal("", branding.PreviewHeader(new BrandingSettings(null, null)));
        Assert.Equal("", branding.PreviewHeader(new BrandingSettings("javascript:alert(1)", "rood")));
    }

    [Fact]
    public async Task Administrator_finds_the_branding_in_the_menu()
    {
        var client = await app.LoggedInClientAsync("huisstijl-menu@hopsakee.test", UserRole.Administrator);

        var page = await client.GetAsync("/admin/huisstijl");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"admin/huisstijl\"", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_branding()
    {
        var client = await app.LoggedInClientAsync("huisstijl-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/huisstijl");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/huisstijl\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    private static IBrandingAdministration Branding(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IBrandingAdministration>();
}
