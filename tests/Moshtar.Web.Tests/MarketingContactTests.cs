using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Marketing;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Wie bij het reserveren aanvinkt dat hij nieuws en acties wil ontvangen, wordt marketingcontact van die verhuurder.
/// Het contact blijft als de winkelkar verdwijnt; de beheerder ziet de lijst en exporteert ze als CSV.
/// </summary>
public sealed class MarketingContactTests(MoshtarApp app) : IClassFixture<MoshtarApp>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        foreach (var tenant in new[] { "hopsakee", "andere" })
        {
            await using var scope = await app.TenantScopeAsync(tenant);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.RentalItems.AnyAsync(i => i.Slug == "prinses")) continue;
            db.RentalItems.Add(new RentalItem
            {
                Slug = "prinses", Stock = 20, Pricing = new Pricing { DayPrice = 120 },
                Translations = [new Translation { Culture = "nl", Name = "Prinsessenkasteel" }],
            });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_news_checkbox_makes_a_marketing_contact_and_without_it_nothing_is_kept()
    {
        await StartCartAsync(MoshtarApp.HopsakeeHost, "Nieuws@Example.test", news: true, cartPath: "/fr/panier");
        await StartCartAsync(MoshtarApp.HopsakeeHost, "geen-nieuws@example.test", news: false);

        var contact = Assert.Single(await ContactsAsync("hopsakee"), c => c.Email == "nieuws@example.test");
        Assert.Equal(("fr", MarketingContactSource.Cart), (contact.Culture, contact.Source));
        Assert.DoesNotContain(await ContactsAsync("hopsakee"), c => c.Email == "geen-nieuws@example.test");
    }

    [Fact]
    public async Task Reserving_with_the_checkbox_makes_the_source_a_reservation()
    {
        var client = await StartCartAsync(MoshtarApp.HopsakeeHost, "reserveert@example.test", news: true);
        (await ReserveAsync(client, "Rita")).EnsureSuccessStatusCode();

        var contact = Assert.Single(await ContactsAsync("hopsakee"), c => c.Email == "reserveert@example.test");
        Assert.Equal(MarketingContactSource.Reservation, contact.Source);
    }

    [Fact]
    public async Task The_contact_stays_when_the_cart_is_deleted()
    {
        await StartCartAsync(MoshtarApp.AndereHost, "blijft@example.test", news: true);

        await using (var scope = await app.TenantScopeAsync("andere"))
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICartHousekeeping>().RunAsync(DateTime.UtcNow.AddDays(8))).CartsDeleted >= 1);

        Assert.Contains(await ContactsAsync("andere"), c => c.Email == "blijft@example.test");
    }

    [Fact]
    public async Task Unticking_in_the_same_cart_withdraws_but_a_new_cart_without_the_tick_keeps_the_consent()
    {
        var client = await StartCartAsync(MoshtarApp.HopsakeeHost, "twijfelt@example.test", news: true);
        await IdentifyAsync(client, "twijfelt@example.test", news: false, cartPath: "/winkelkar?stap=email");
        Assert.DoesNotContain(await ContactsAsync("hopsakee"), c => c.Email == "twijfelt@example.test");

        await StartCartAsync(MoshtarApp.HopsakeeHost, "trouw@example.test", news: true);
        await StartCartAsync(MoshtarApp.HopsakeeHost, "trouw@example.test", news: false);
        Assert.Contains(await ContactsAsync("hopsakee"), c => c.Email == "trouw@example.test");
    }

    [Fact]
    public async Task The_administrator_sees_the_list_and_exports_it_as_csv_without_contacts_of_another_rental_company()
    {
        await StartCartAsync(MoshtarApp.HopsakeeHost, "=csv@example.test", news: true);
        await StartCartAsync(MoshtarApp.AndereHost, "van-andere@example.test", news: true);
        var admin = await app.LoggedInClientAsync("marketing-beheerder@hopsakee.test", UserRole.Administrator);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/marketingcontacten"));
        Assert.Contains("=csv@example.test", page);
        Assert.DoesNotContain("van-andere@example.test", page);

        var response = await admin.GetAsync("/admin/marketingcontacten.csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal("E-mailadres;Taal;Toestemming;Herkomst", lines[0]);
        // Een adres dat met = begint, wordt nooit een formule in Excel.
        Assert.Contains(lines, l => l.StartsWith("'=csv@example.test;nl;") && l.EndsWith(";Winkelkar"));
        Assert.DoesNotContain(lines, l => l.Contains("van-andere@example.test"));
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_marketing_contacts()
    {
        var client = await app.LoggedInClientAsync("marketing-medewerker@hopsakee.test", UserRole.Staff);

        foreach (var path in new[] { "/admin/marketingcontacten", "/admin/marketingcontacten.csv" })
        {
            var response = await client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.Redirect)
                response = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.DoesNotContain("href=\"admin/marketingcontacten\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_login_the_export_is_not_available()
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync("/admin/marketingcontacten.csv");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/inloggen", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_privacy_statement_explains_the_marketing_contacts()
    {
        var html = await PageAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "/privacy");
        Assert.Contains("Nieuws en acties", html);
        Assert.Contains("nieuws en acties van Hopsakee.fun", html);
    }

    private async Task<HttpClient> StartCartAsync(string host, string email, bool news, string cartPath = "/winkelkar")
    {
        var client = app.CreateClient(host);
        var day = TestReservations.FutureDay(40);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        await IdentifyAsync(client, email, news: news, cartPath: cartPath);
        return client;
    }

    private async Task<List<MarketingContact>> ContactsAsync(string tenant)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MarketingContacts.AsNoTracking().ToListAsync();
    }
}
