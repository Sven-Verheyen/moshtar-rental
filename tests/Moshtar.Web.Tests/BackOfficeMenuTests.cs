using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Customers;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

/// <summary>
/// Het menu van het back office in zes onderdelen: alleen wat de rol mag openen, alleen het onderdeel van de huidige pagina open,
/// en bovenaan elke pagina een kruimelpad.
/// </summary>
public sealed class BackOfficeMenuTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Administrator_sees_the_six_parts_in_order_with_their_pages()
    {
        var client = await app.LoggedInClientAsync("menu-onderdelen-beheerder@hopsakee.test", UserRole.Administrator);

        var menu = Menu(await client.GetStringAsync("/admin/planning"));

        Assert.Equal(["Planning", "Reservaties", "Klantenbeheer", "Artikelbeheer", "Website en mails", "Instellingen"], menu.Select(p => p.Title));
        Assert.Equal(
        [
            ["admin/planning", "admin/planning/leverlijst", "admin/planning/instellingen"],
            ["admin/reservaties", "admin/reservaties/nieuw", "admin/tekorten", "admin/winkelkarren"],
            ["admin/klanten", "admin/marketingcontacten", "admin/klanten/reviewvragen"],
            ["admin/artikelen", "admin/pakketten", "admin/categorieen", "admin/uitgelicht", "admin/prijsregels", "admin/blokkeringen"],
            ["admin/websiteteksten", "admin/huisstijl", "admin/mailsjablonen"],
            ["admin/bedrijfsgegevens", "admin/gebruikers"],
        ], menu.Select(p => p.Pages));
    }

    [Fact]
    public async Task Staff_sees_only_planning_reservations_and_customers()
    {
        var client = await app.LoggedInClientAsync("menu-onderdelen-medewerker@hopsakee.test", UserRole.Staff);

        var menu = Menu(await client.GetStringAsync("/admin/reservaties"));

        Assert.Equal(["Planning", "Reservaties", "Klantenbeheer"], menu.Select(p => p.Title));
        Assert.Equal(["admin/planning", "admin/planning/leverlijst"], menu[0].Pages);
        Assert.Equal(["admin/reservaties", "admin/reservaties/nieuw", "admin/tekorten", "admin/winkelkarren"], menu[1].Pages);
        Assert.Equal(["admin/klanten"], menu[2].Pages);
    }

    [Theory]
    [InlineData("/admin", "Planning")]
    [InlineData("/admin/planning", "Planning")]
    [InlineData("/admin/reservaties/nieuw", "Reservaties")]
    [InlineData("/admin/tekorten", "Reservaties")]
    [InlineData("/admin/klanten/nieuw", "Klantenbeheer")]
    [InlineData("/admin/klanten/importeren", "Klantenbeheer")]
    [InlineData("/admin/klanten/reviewvragen", "Klantenbeheer")]
    [InlineData("/admin/planning/instellingen", "Planning")]
    [InlineData("/admin/artikelen/nieuw", "Artikelbeheer")]
    [InlineData("/admin/pakketten/nieuw", "Artikelbeheer")]
    [InlineData("/admin/mailsjablonen", "Website en mails")]
    [InlineData("/admin/gebruikers", "Instellingen")]
    public async Task Only_the_part_of_the_current_page_is_open(string url, string part)
    {
        var client = await app.LoggedInClientAsync("menu-open-beheerder@hopsakee.test", UserRole.Administrator);

        var menu = Menu(await client.GetStringAsync(url));

        Assert.Equal([part], menu.Where(p => p.Open).Select(p => p.Title));
    }

    [Fact]
    public async Task Every_page_in_the_menu_opens_at_its_existing_address()
    {
        var client = await app.LoggedInClientAsync("menu-adressen-beheerder@hopsakee.test", UserRole.Administrator);

        var pages = Menu(await client.GetStringAsync("/admin")).SelectMany(p => p.Pages).ToList();

        Assert.Equal(21, pages.Count);
        foreach (var page in pages)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/{page}")).StatusCode);
    }

    [Fact]
    public async Task The_shortage_count_shows_next_to_shortages_and_next_to_the_closed_part_reservations()
    {
        var client = await app.LoggedInClientAsync("menu-tekorten-medewerker@hopsakee.test", UserRole.Staff);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var items = scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();
            var castle = await items.CreateAsync(new([new RentalItemText("nl", "Menukasteel")], null, 1, 100, null,
                new ItemSpecs(null, null, null, null, null, null, false), "menukasteel"));
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
                new DateRange(TestReservations.FutureDay(), TestReservations.FutureDay()), [new ReservationLineRequest(castle, null, 1)],
                new ReservationCustomer("Mia", "Klant", "mia@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
                DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
            await scope.ServiceProvider.GetRequiredService<IBlockoutAdministration>().CreateAsync(new(castle, 1, TestReservations.FutureDay(), null, "Lek"));
        }

        var planning = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/planning"));
        var shortages = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/tekorten"));

        Assert.Matches("data-shortage-count=\"[1-9]", planning);
        Assert.Matches("data-part-shortage-count=\"[1-9]", planning);
        Assert.Matches("data-shortage-count=\"[1-9]", shortages);
        Assert.DoesNotContain("data-part-shortage-count", shortages);
    }

    [Fact]
    public async Task A_menu_page_shows_its_part_and_itself_in_the_breadcrumb()
    {
        var client = await app.LoggedInClientAsync("kruimelpad-medewerker@hopsakee.test", UserRole.Staff);

        Assert.Equal(["Reservaties", "Tekorten"], Breadcrumb(await client.GetStringAsync("/admin/tekorten")));
        Assert.Equal(["Planning", "Planning"], Breadcrumb(await client.GetStringAsync("/admin")));
    }

    [Fact]
    public async Task A_subpage_shows_its_menu_page_as_a_link_and_the_name_of_what_is_open()
    {
        var user = await app.CreateUserAsync("hopsakee", "kruimelpad-beheerder@hopsakee.test");
        var client = await app.LoggedInClientAsync("kruimelpad-beheerder@hopsakee.test", UserRole.Administrator);
        Guid customer, item;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            customer = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().AddAsync(new CustomerDetails(
                CustomerKind.Private, "Kruimel", "Klant", "kruimel@example.test", "0499 88 77 66",
                new Address { Street = "Heidestraat 12", PostalCode = "2950", City = "Kapellen" }, "nl"), user.Id)).CustomerId!.Value;
            item = await scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>().CreateAsync(new([new RentalItemText("nl", "Springkasteel Piraat")],
                null, 1, 100, null, new ItemSpecs(null, null, null, null, null, null, false), "kruimel-piraat"));
        }

        Assert.Equal(["Klantenbeheer", "Klanten → admin/klanten", "Importeren"], Breadcrumb(await client.GetStringAsync("/admin/klanten/importeren")));
        Assert.Equal(["Klantenbeheer", "Klanten → admin/klanten", "Nieuwe klant"], Breadcrumb(await client.GetStringAsync("/admin/klanten/nieuw")));
        Assert.Equal(["Klantenbeheer", "Klanten → admin/klanten", "Kruimel Klant"], Breadcrumb(await client.GetStringAsync($"/admin/klanten/{customer}")));
        Assert.Equal(["Artikelbeheer", "Artikelen → admin/artikelen", "Springkasteel Piraat"], Breadcrumb(await client.GetStringAsync($"/admin/artikelen/{item}")));
        Assert.Equal(["Artikelbeheer", "Pakketten → admin/pakketten", "Nieuw pakket"], Breadcrumb(await client.GetStringAsync("/admin/pakketten/nieuw")));
        Assert.Equal(["Reservaties", "Nieuwe reservatie"], Breadcrumb(await client.GetStringAsync("/admin/reservaties/nieuw")));
    }

    /// <summary>De stappen van het kruimelpad; een klikbare stap als "tekst → adres".</summary>
    internal static List<string> Breadcrumb(string html)
    {
        html = WebUtility.HtmlDecode(html);
        var start = html.IndexOf("data-breadcrumb", StringComparison.Ordinal);
        Assert.True(start >= 0, "Geen kruimelpad gevonden.");
        var nav = html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
        return Regex.Matches(nav, "<li class=\"mud-breadcrumb-item[^\"]*\"[^>]*>(.*?)</li>", RegexOptions.Singleline)
            .Select(m =>
            {
                var text = Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", ""), @"\s+", " ").Trim();
                var href = Regex.Match(m.Groups[1].Value, "<a [^>]*href=\"([^\"]+)\"");
                return href.Success ? $"{text} → {href.Groups[1].Value}" : text;
            })
            .ToList();
    }

    private sealed record MenuPart(string Title, bool Open, string[] Pages);

    /// <summary>De onderdelen van het menu, in volgorde, met de adressen van hun pagina's.</summary>
    private static List<MenuPart> Menu(string html)
    {
        html = WebUtility.HtmlDecode(html);
        var parts = Regex.Matches(html, "data-menu-part=\"([^\"]+)\" data-menu-open=\"(true|false)\"").ToList();
        var menuEnd = html.IndexOf("data-menu-end", StringComparison.Ordinal);
        return parts.Select((part, i) =>
        {
            var end = i + 1 < parts.Count ? parts[i + 1].Index : menuEnd;
            var block = html[part.Index..end];
            var pages = Regex.Matches(block, "href=\"(admin[^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();
            return new MenuPart(part.Groups[1].Value, part.Groups[2].Value == "true", pages);
        }).ToList();
    }
}
