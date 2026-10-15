using System.Net;
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
/// De beheerder licht artikelen en pakketten uit en kiest hun volgorde. Op de home vervangen ze de blok "Populair"; is
/// niets uitgelicht, dan blijft de home zoals ze was.
/// </summary>
public sealed class FeaturedTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task The_home_shows_the_featured_items_and_bundles_in_their_order_instead_of_popular()
    {
        var tenant = await NewTenantAsync("uitgelicht-home");
        await using (var scope = await app.TenantScopeAsync("uitgelicht-home"))
        {
            await Featured(scope).SetFeaturedAsync(tenant.Mill, true);
            await Featured(scope).SetFeaturedAsync(tenant.Party, true);
            await Featured(scope).SetFeaturedAsync(tenant.Pirate, true);
            await Featured(scope).ReorderAsync([tenant.Party, tenant.Pirate, tenant.Mill]);
        }

        var html = await HomeAsync("uitgelicht-home", "/");

        Assert.DoesNotContain("<h2>Populair</h2>", html);
        Assert.Contains("<h2>Uitgelicht</h2>", html);
        var order = new[] { "Feestpakket", "Piratenkasteel", "Molen" }.Select(name => html.IndexOf($">{name}</a></h3>")).ToList();
        Assert.All(order, position => Assert.True(position > 0));
        Assert.True(order[0] < order[1] && order[1] < order[2], "In de volgorde van de beheerder.");
        // Het prinsessenkasteel is niet uitgelicht.
        Assert.DoesNotContain(">Prinsessenkasteel</a></h3>", html);

        Assert.Contains("<h2>Featured</h2>", await HomeAsync("uitgelicht-home", "/en"));
        Assert.Contains("<h2>À la une</h2>", WebUtility.HtmlDecode(await HomeAsync("uitgelicht-home", "/fr")));
    }

    [Fact]
    public async Task With_a_rental_period_the_home_shows_the_featured_first_with_their_availability_and_then_the_whole_offer()
    {
        var tenant = await NewTenantAsync("uitgelicht-periode");
        var day = TestReservations.FutureDay(10);
        await using (var scope = await app.TenantScopeAsync("uitgelicht-periode"))
        {
            await Featured(scope).SetFeaturedAsync(tenant.Mill, true);
            await Featured(scope).SetFeaturedAsync(tenant.Pirate, true);
            // De enige molen is die dag al gereserveerd.
            var reserved = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
                new DateRange(day, day), [new ReservationLineRequest(tenant.Mill, null, 1)],
                new ReservationCustomer("Test", "Klant", "klant@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
                DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
            Assert.NotNull(reserved.Reservation);
        }

        var html = await HomeAsync("uitgelicht-periode", $"/?van={day:yyyy-MM-dd}");

        var featuredStart = html.IndexOf("<h2>Uitgelicht</h2>");
        var offerStart = html.IndexOf("<h2>Ons aanbod voor");
        Assert.True(featuredStart > 0 && offerStart > featuredStart, "Eerst de uitgelichte, dan het hele aanbod.");
        var featured = html[featuredStart..offerStart];
        Assert.Contains(">Piratenkasteel</a></h3>", featured);
        Assert.Contains(">Molen</a></h3>", featured);
        Assert.DoesNotContain(">Prinsessenkasteel</a></h3>", featured);
        Assert.Contains("Niet vrij op deze datum", featured[featured.IndexOf(">Molen</a></h3>")..]);
        Assert.DoesNotContain("Niet vrij op deze datum", featured[..featured.IndexOf(">Molen</a></h3>")]);
        Assert.Contains($"?van={day:yyyy-MM-dd}", featured);

        var offer = html[offerStart..];
        // Het feestpakket bevat de molen, dus ook dat is niet vrij.
        Assert.Contains("<p class=\"muted\">2 van de 4 vrij</p>", offer);
        Assert.Contains(">Prinsessenkasteel</a></h3>", offer);
        Assert.Contains(">Molen</a></h3>", offer);
    }

    [Fact]
    public async Task A_switched_off_item_and_a_bundle_hidden_by_it_do_not_appear_on_the_home()
    {
        var tenant = await NewTenantAsync("uitgelicht-uitgezet");
        await using (var scope = await app.TenantScopeAsync("uitgelicht-uitgezet"))
        {
            await Featured(scope).SetFeaturedAsync(tenant.Mill, true);
            await Featured(scope).SetFeaturedAsync(tenant.Party, true);
            await Featured(scope).SetFeaturedAsync(tenant.Pirate, true);
            // Het feestpakket bevat de molen: het verdwijnt mee van de reservatiewebsite.
            await Items(scope).SetActiveAsync(tenant.Mill, false);

            Assert.Equal(
                [("Molen", false), ("Feestpakket", false), ("Piratenkasteel", true)],
                (await Featured(scope).GetAllAsync()).Select(f => (f.Name, f.IsVisible)));
        }

        var html = await HomeAsync("uitgelicht-uitgezet", "/");

        Assert.Contains("<h2>Uitgelicht</h2>", html);
        Assert.Contains(">Piratenkasteel</a></h3>", html);
        Assert.DoesNotContain(">Molen</a></h3>", html);
        Assert.DoesNotContain(">Feestpakket</a></h3>", html);
    }

    [Fact]
    public async Task Without_featured_items_the_home_shows_popular_as_before()
    {
        var tenant = await NewTenantAsync("uitgelicht-geen");
        await using (var scope = await app.TenantScopeAsync("uitgelicht-geen"))
        {
            await Featured(scope).SetFeaturedAsync(tenant.Mill, true);
            await Featured(scope).SetFeaturedAsync(tenant.Mill, false);
            Assert.Empty(await Featured(scope).GetAllAsync());
        }

        var html = await HomeAsync("uitgelicht-geen", "/");

        Assert.Contains("<h2>Populair</h2>", html);
        Assert.DoesNotContain("<h2>Uitgelicht</h2>", html);
        Assert.Contains(">Feestpakket</a></h3>", html);
        Assert.Contains(">Prinsessenkasteel</a></h3>", html);
    }

    [Fact]
    public async Task Featured_items_and_bundles_form_one_list_where_new_ones_come_last_and_the_order_can_change()
    {
        var tenant = await NewTenantAsync("uitgelicht-lijst");
        await using var scope = await app.TenantScopeAsync("uitgelicht-lijst");

        await Featured(scope).SetFeaturedAsync(tenant.Pirate, true);
        await Featured(scope).SetFeaturedAsync(tenant.Party, true);
        await Featured(scope).SetFeaturedAsync(tenant.Mill, true);
        // Opnieuw uitlichten verandert de plaats niet.
        await Featured(scope).SetFeaturedAsync(tenant.Pirate, true);
        Assert.Equal(
            [(tenant.Pirate, CatalogEntryKind.Item, "Piratenkasteel", true), (tenant.Party, CatalogEntryKind.Bundle, "Feestpakket", true), (tenant.Mill, CatalogEntryKind.Item, "Molen", true)],
            (await Featured(scope).GetAllAsync()).Select(f => (f.Id, f.Kind, f.Name, f.IsVisible)));

        await Featured(scope).ReorderAsync([tenant.Mill, tenant.Pirate, tenant.Party]);
        await Featured(scope).SetFeaturedAsync(tenant.Pirate, false);
        await Featured(scope).SetFeaturedAsync(tenant.Princess, true);
        Assert.Equal(["Molen", "Feestpakket", "Prinsessenkasteel"], (await Featured(scope).GetAllAsync()).Select(f => f.Name));

        // De vlag staat ook bij het artikel en het pakket in hun overzicht.
        Assert.Equal(["Molen", "Prinsessenkasteel"], (await Items(scope).GetAllAsync()).Where(i => i.IsFeatured).Select(i => i.Name).Order());
        Assert.True(Assert.Single(await Bundles(scope).GetAllAsync()).IsFeatured);

        // Een volgorde die niet (meer) klopt, wordt geweigerd.
        var refused = await Assert.ThrowsAsync<FeaturedException>(() => Featured(scope).ReorderAsync([tenant.Mill, tenant.Party]));
        Assert.Equal("De uitgelichte artikelen en pakketten zijn intussen gewijzigd; laad de pagina opnieuw.", refused.Message);
        await Assert.ThrowsAsync<FeaturedException>(() => Featured(scope).SetFeaturedAsync(Guid.NewGuid(), true));
    }

    [Fact]
    public async Task Only_an_administrator_opens_the_featured_page()
    {
        var staff = await app.LoggedInClientAsync("uitgelicht-medewerker@hopsakee.test", UserRole.Staff);
        var administrator = await app.LoggedInClientAsync("uitgelicht-beheerder@hopsakee.test", UserRole.Administrator);

        var response = await staff.GetAsync("/admin/uitgelicht");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await staff.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync("/admin/uitgelicht")).StatusCode);
        Assert.Contains("href=\"admin/uitgelicht\"", await administrator.GetStringAsync("/admin/reservaties"));
    }

    private sealed record TestTenant(Guid Princess, Guid Pirate, Guid Mill, Guid Party);

    /// <summary>Twee springkastelen en een molen in twee categorieën, en een feestpakket met het piratenkasteel en de molen.</summary>
    private async Task<TestTenant> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen")] };
        var games = new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen")] };
        db.Categories.AddRange(castles, games);
        await db.SaveChangesAsync();
        var princess = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Prinsessenkasteel")], castles.Id, 1, 100, null, NoSpecs));
        var pirate = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Piratenkasteel")], castles.Id, 1, 120, null, NoSpecs));
        var mill = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Molen")], games.Id, 1, 30, null, NoSpecs));
        var party = await Bundles(scope).CreateAsync(new BundleInput([new("nl", "Feestpakket")], [new(pirate, 1), new(mill, 1)], 140, null));
        return new TestTenant(princess, pirate, mill, party);
    }

    private async Task<string> HomeAsync(string tenant, string path) =>
        await app.CreateClient($"{tenant}.test").GetStringAsync(path);

    private static IFeaturedAdministration Featured(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IFeaturedAdministration>();

    private static IRentalItemAdministration Items(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IBundleAdministration Bundles(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IBundleAdministration>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
