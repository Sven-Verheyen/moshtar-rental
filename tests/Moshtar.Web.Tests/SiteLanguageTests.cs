using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De taal van de reservatiewebsite staat in de URL (ADR 0004): de standaardtaal zonder voorvoegsel,
/// de andere talen met /fr/ of /en/. Elke verhuurder heeft één hoofddomein waarnaar zijn andere domeinen doorsturen.
/// </summary>
public sealed class SiteLanguageTests(MoshtarApp app) : IClassFixture<MoshtarApp>, IAsyncLifetime
{
    private const string Slug = "taal-kasteel";

    public async Task InitializeAsync()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.RentalItems.AnyAsync(i => i.Slug == Slug)) return;
        db.RentalItems.Add(new RentalItem
        {
            Slug = Slug, Stock = 1, Pricing = new Pricing { DayPrice = 100 },
            Translations = [T("nl", "Kasteel"), T("fr", "Château"), T("en", "Castle")],
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/huren/" + Slug, "nl", "Kasteel", "Reserveren")]
    [InlineData("/fr/louer/" + Slug, "fr", "Château", "Réserver")]
    [InlineData("/en/rent/" + Slug, "en", "Castle", "Reserve")]
    public async Task The_language_comes_from_the_url(string path, string culture, string name, string reserve)
    {
        var html = WebUtility.HtmlDecode(await PageAsync(path));

        Assert.Contains($"<html lang=\"{culture}\">", html);
        Assert.Contains($"<h1>{name}</h1>", html);
        Assert.Contains($"<h2>{reserve}</h2>", html);
    }

    [Fact]
    public async Task A_language_cookie_no_longer_changes_the_language()
    {
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dfr%7Cuic%3Dfr");

        var html = await client.GetStringAsync("/huren/" + Slug);

        Assert.Contains("<html lang=\"nl\">", html);
    }

    [Fact]
    public async Task Every_page_has_a_canonical_url_and_links_to_each_language()
    {
        var html = await PageAsync("/fr/louer/" + Slug);

        Assert.Contains($"<link rel=\"canonical\" href=\"https://{MoshtarApp.HopsakeeHost}/fr/louer/{Slug}\" />", html);
        Assert.Contains($"<link rel=\"alternate\" hreflang=\"nl\" href=\"https://{MoshtarApp.HopsakeeHost}/huren/{Slug}\" />", html);
        Assert.Contains($"<link rel=\"alternate\" hreflang=\"fr\" href=\"https://{MoshtarApp.HopsakeeHost}/fr/louer/{Slug}\" />", html);
        Assert.Contains($"<link rel=\"alternate\" hreflang=\"en\" href=\"https://{MoshtarApp.HopsakeeHost}/en/rent/{Slug}\" />", html);
        Assert.Contains($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://{MoshtarApp.HopsakeeHost}/huren/{Slug}\" />", html);
    }

    [Fact]
    public async Task The_home_page_links_to_the_home_page_in_each_language()
    {
        var html = await PageAsync("/en");

        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"https://{MoshtarApp.HopsakeeHost}/en\" />", html);
        Assert.Contains($"<link rel=\"alternate\" hreflang=\"nl\" href=\"https://{MoshtarApp.HopsakeeHost}/\" />", html);
        Assert.Contains($"href=\"/en/rent/{Slug}\"", html);
    }

    [Fact]
    public async Task The_language_choice_leads_to_the_same_page_in_the_other_language()
    {
        var html = await PageAsync("/huren/" + Slug);

        Assert.Contains($"href=\"/fr/louer/{Slug}\"", html);
        Assert.Contains($"href=\"/en/rent/{Slug}\"", html);
    }

    [Theory]
    [InlineData("/fr/huren/" + Slug, "/fr/louer/" + Slug)]
    [InlineData("/louer/" + Slug, "/huren/" + Slug)]
    [InlineData("/nl/huren/" + Slug, "/huren/" + Slug)]
    [InlineData("/nl", "/")]
    [InlineData("/en/huren/" + Slug + "?van=2026-06-12", "/en/rent/" + Slug + "?van=2026-06-12")]
    public async Task A_url_in_the_wrong_form_moves_permanently_to_the_right_one(string path, string target)
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_language_the_rental_company_does_not_offer_is_not_found()
    {
        await app.CreateTenantAsync("eentalig", "eentalig.test");
        await UpdateTenantAsync("eentalig", t => t.SupportedCultures = "nl");

        var client = app.CreateClient("eentalig.test");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/fr")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.DoesNotContain("hreflang=\"fr\"", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task The_back_office_has_no_language_prefix()
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync("/fr/admin/inloggen");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Other_domains_move_permanently_to_the_primary_domain()
    {
        await CreateTenantWithHostsAsync("hoofd", "hoofd.test", "www.hoofd.test");

        var response = await app.CreateClient("www.hoofd.test").GetAsync("/fr/louer/iets?van=2026-06-12");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("http://hoofd.test/fr/louer/iets?van=2026-06-12", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_canonical_url_is_on_the_primary_domain()
    {
        await CreateTenantWithHostsAsync("kanon", "kanon.test", "localhost");

        var html = await app.CreateClient("localhost").GetStringAsync("/");

        Assert.Contains("<link rel=\"canonical\" href=\"https://kanon.test/\" />", html);
    }

    [Fact]
    public async Task Localhost_is_never_sent_to_the_primary_domain()
    {
        await CreateTenantWithHostsAsync("lokaal", "lokaal.test", "127.0.0.1");

        var response = await app.CreateClient("127.0.0.1").GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_reservation_gets_the_language_of_the_url()
    {
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        var day = TestReservations.FutureDay(320);
        var path = "/en/rent/" + Slug;

        var form = await client.GetStringAsync(path);
        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "reservation",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Form.StartDate"] = day.ToString("yyyy-MM-dd"),
            ["Form.EndDate"] = day.ToString("yyyy-MM-dd"),
            ["Form.Quantity"] = "1",
            ["Form.FirstName"] = "Emma",
            ["Form.LastName"] = "English",
            ["Form.Email"] = "emma@example.test",
            ["Form.Street"] = "High Street 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Pickup",
        }));
        response.EnsureSuccessStatusCode();

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reservation = await db.Reservations.SingleAsync(r => r.Customer!.Email == "emma@example.test");
        Assert.Equal("en", reservation.Culture);
    }

    private Task<string> PageAsync(string path) => app.CreateClient(MoshtarApp.HopsakeeHost).GetStringAsync(path);

    private async Task CreateTenantWithHostsAsync(string slug, string primary, string other)
    {
        await app.CreateTenantAsync(slug, primary);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.Include(t => t.Hosts).SingleAsync(t => t.Slug == slug);
        tenant.Hosts.Single().IsPrimary = true;
        db.Add(new TenantHost { TenantId = tenant.Id, Hostname = other });
        await db.SaveChangesAsync();
    }

    private async Task UpdateTenantAsync(string slug, Action<Tenant> change)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Tenants.SingleAsync(t => t.Slug == slug));
        await db.SaveChangesAsync();
    }

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
