using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder geeft blokkeringen in: een aantal exemplaren van een artikel is vanaf een begindatum, tot een
/// einddatum of zonder einddatum, niet verhuurbaar, bijvoorbeeld omdat het kapot is.
/// </summary>
public sealed class BlockoutAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task A_blockout_without_end_date_makes_the_item_unavailable_on_every_later_day_until_an_end_date_is_entered()
    {
        var day = TestReservations.FutureDay();
        Guid jungle;
        Guid blockout;
        await using (var scope = await NewTenantAsync("blokkering-open"))
        {
            jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 1));
            blockout = (await Blockouts(scope).CreateAsync(new(jungle, 1, day, null, "Gescheurd"))).Id;

            // Het back office reserveert het ook niet meer, ook jaren later niet.
            Assert.NotEmpty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day.AddYears(5), day.AddYears(5)), [new(jungle, null, 1)]));
            Assert.Empty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day.AddDays(-1), day.AddDays(-1)), [new(jungle, null, 1)]));
        }
        Assert.Contains("Niet vrij op", await DetailAsync("blokkering-open", day.AddDays(300)));
        Assert.DoesNotContain("Niet vrij op", await DetailAsync("blokkering-open", day.AddDays(-1)));

        await using (var scope = await TenantScopeAsync("blokkering-open"))
            await Blockouts(scope).SaveAsync(blockout, new(jungle, 1, day, day.AddDays(7), "Gescheurd, hersteld tegen volgende week"));

        Assert.Contains("Niet vrij op", await DetailAsync("blokkering-open", day.AddDays(7)));
        Assert.DoesNotContain("Niet vrij op", await DetailAsync("blokkering-open", day.AddDays(8)));
    }

    [Fact]
    public async Task Only_the_blocked_units_are_unavailable()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("blokkering-aantal");
        var games = await Items(scope).CreateAsync(Input("Sjoelbak", stock: 3));

        await Blockouts(scope).CreateAsync(new(games, 2, day, null, null));

        Assert.Empty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day, day), [new(games, null, 1)]));
        Assert.NotEmpty(await Reservations(scope).CheckAvailabilityAsync(new DateRange(day, day), [new(games, null, 2)]));
    }

    [Fact]
    public async Task Blockouts_are_created_changed_and_deleted_and_only_current_ones_are_listed()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("blokkering-beheer");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 2));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", stock: 1));
        var today = DateOnly.FromDateTime(DateTime.Today);

        var later = await Blockouts(scope).CreateAsync(new(jungle, 2, day.AddDays(5), day.AddDays(6), " Onderhoud "));
        var open = await Blockouts(scope).CreateAsync(new(pirate, 1, today.AddDays(-30), null, ""));
        await Blockouts(scope).CreateAsync(new(jungle, 1, today.AddDays(-10), today.AddDays(-1), "Voorbij"));

        var all = await Blockouts(scope).GetCurrentAsync();
        Assert.Equal([open.Id, later.Id], all.Select(b => b.Id));
        Assert.Equal(("Piraat", new BlockoutInput(pirate, 1, today.AddDays(-30), null, null)), (all[0].RentalItemName, all[0].Input));
        Assert.Equal(new BlockoutInput(jungle, 2, day.AddDays(5), day.AddDays(6), "Onderhoud"), all[1].Input);
        Assert.Equal([later.Id], (await Blockouts(scope).GetCurrentAsync(jungle)).Select(b => b.Id));

        await Blockouts(scope).SaveAsync(later.Id, new(jungle, 1, day.AddDays(5), null, "Kapot"));
        Assert.Equal(new BlockoutInput(jungle, 1, day.AddDays(5), null, "Kapot"), (await Blockouts(scope).GetCurrentAsync(jungle)).Single().Input);

        await Blockouts(scope).DeleteAsync(later.Id);
        Assert.Empty(await Blockouts(scope).GetCurrentAsync(jungle));
    }

    [Fact]
    public async Task An_invalid_blockout_is_refused()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("blokkering-ongeldig");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 2));

        var refused = await Assert.ThrowsAsync<BlockoutException>(() =>
            Blockouts(scope).CreateAsync(new(jungle, 0, day, day.AddDays(-1), new string('x', 201))));
        Assert.Equal([
            "Blokkeer minstens 1 exemplaar.",
            "De einddatum ligt voor de begindatum.",
            "De reden is langer dan 200 tekens.",
        ], refused.Problems);

        refused = await Assert.ThrowsAsync<BlockoutException>(() => Blockouts(scope).CreateAsync(new(jungle, 3, day, null, null)));
        Assert.Equal(["Van \"Jungle\" heb je maar 2 exemplaren."], refused.Problems);

        refused = await Assert.ThrowsAsync<BlockoutException>(() => Blockouts(scope).CreateAsync(new(Guid.NewGuid(), 1, day, null, null)));
        Assert.Equal(["Dit artikel bestaat niet (meer)."], refused.Problems);
        Assert.Empty(await Blockouts(scope).GetCurrentAsync());
    }

    [Fact]
    public async Task A_blockout_over_reservations_is_saved_with_a_warning_per_day_from_today()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("blokkering-tekort");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 2));
        var first = await ReserveAsync(scope, jungle, day, day.AddDays(1));
        await ReserveAsync(scope, jungle, day.AddDays(20), day.AddDays(20));

        var saved = await Blockouts(scope).CreateAsync(new(jungle, 2, day.AddDays(1), null, "Allebei kapot"));

        Assert.Equal([day.AddDays(1), day.AddDays(20)], saved.Shortages.Select(s => s.Day));
        Assert.Equal((0, 1), (saved.Shortages[0].Rentable, saved.Shortages[0].Reserved));
        Assert.Equal([first.Id], saved.Shortages[0].ReservationIds);
        Assert.Single(await Blockouts(scope).GetCurrentAsync(jungle));

        var fewer = await Blockouts(scope).SaveAsync(saved.Id, new(jungle, 1, day.AddDays(1), null, "Eén kapot"));
        Assert.Empty(fewer.Shortages);
    }

    [Fact]
    public async Task The_warning_after_saving_lists_only_the_shortages_the_save_caused_or_worsened()
    {
        var day = TestReservations.FutureDay(40);
        await using var scope = await NewTenantAsync("blokkering-veroorzaakt");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 2));
        await ReserveAsync(scope, jungle, day, day);
        await ReserveAsync(scope, jungle, day.AddDays(5), day.AddDays(5));
        await ReserveAsync(scope, jungle, day.AddDays(10), day.AddDays(10));
        await ReserveAsync(scope, jungle, day.AddDays(10), day.AddDays(10));
        // Er is al een tekort op de tiende dag.
        Assert.Equal([day.AddDays(10)], (await Blockouts(scope).CreateAsync(new(jungle, 1, day.AddDays(10), day.AddDays(10), "Lek"))).Shortages.Select(s => s.Day));

        // Een nieuwe blokkering op de eerste dag: alleen dat nieuwe tekort, niet het bestaande.
        var saved = await Blockouts(scope).CreateAsync(new(jungle, 2, day, day, "Allebei weg"));
        Assert.Equal([day], saved.Shortages.Select(s => s.Day));

        // Ze verschuiven: de eerste dag is opgelost, de tiende dag wordt erger (nog 0 verhuurbaar in plaats van 1).
        saved = await Blockouts(scope).SaveAsync(saved.Id, new(jungle, 1, day.AddDays(1), day.AddDays(10), "Eén weg"));
        Assert.Equal([(day.AddDays(10), 0)], saved.Shortages.Select(s => (s.Day, s.Rentable)));

        // Een lagere voorraad: een nieuw tekort op de vijfde dag, de tiende dag blijft even erg.
        var lowered = await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 1));
        Assert.Equal([day.AddDays(5)], lowered.Select(s => s.Day));
        Assert.Empty(await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 1)));
    }

    [Fact]
    public async Task Administrator_manages_blockouts_on_their_own_page_and_on_the_item_page()
    {
        var client = await app.LoggedInClientAsync("blokkeringen-beheerder@hopsakee.test", UserRole.Administrator);
        Guid item;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            item = await Items(scope).CreateAsync(Input("Blokkeerkasteel", stock: 1, slug: "blokkeerkasteel"));
            await Blockouts(scope).CreateAsync(new(item, 1, TestReservations.FutureDay(), null, "Lek in de bodem"));
        }

        var page = await client.GetAsync("/admin/blokkeringen");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"admin/blokkeringen\"", html);
        Assert.Contains("Blokkeerkasteel", html);
        Assert.Contains("Lek in de bodem", html);
        Assert.Contains("geen einddatum", html);

        var itemPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/artikelen/{item}"));
        Assert.Contains("Blokkeringen", itemPage);
        Assert.Contains("Lek in de bodem", itemPage);
    }

    [Fact]
    public async Task Staff_cannot_manage_blockouts()
    {
        var client = await app.LoggedInClientAsync("blokkeringen-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/blokkeringen");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/blokkeringen\"", await client.GetStringAsync("/admin/reservaties"));
    }

    private async Task<string> DetailAsync(string tenantSlug, DateOnly day) =>
        WebUtility.HtmlDecode(await app.CreateClient($"{tenantSlug}.test").GetStringAsync($"/huren/jungle?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}"));

    private static async Task<Reservation> ReserveAsync(AsyncServiceScope scope, Guid itemId, DateOnly start, DateOnly end)
    {
        var result = await Reservations(scope).ReserveAsync(new ReservationRequest(
            new DateRange(start, end),
            [new ReservationLineRequest(itemId, null, 1)],
            new ReservationCustomer("Test", "Klant", "klant@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private async Task<AsyncServiceScope> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        return await TenantScopeAsync(slug);
    }

    private Task<AsyncServiceScope> TenantScopeAsync(string slug) => app.TenantScopeAsync(slug);

    private static RentalItemInput Input(string name, int stock, string? slug = null) =>
        new([new RentalItemText("nl", name)], null, stock, 100, null, NoSpecs, slug);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IBlockoutAdministration Blockouts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBlockoutAdministration>();

    private static IReservationService Reservations(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationService>();
}
