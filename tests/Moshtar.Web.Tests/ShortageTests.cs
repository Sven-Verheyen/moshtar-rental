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
/// De lijst Tekorten: per artikel de dagen vanaf vandaag waarop meer gereserveerd is dan verhuurbaar, met de getroffen
/// reservaties, ook als het artikel in een gereserveerd pakket zit. Berekend, niet bewaard: een opgelost tekort
/// verdwijnt vanzelf.
/// </summary>
public sealed class ShortageTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task Shortages_are_listed_per_item_with_every_affected_reservation_also_via_a_bundle()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("tekorten-lijst");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 2));
        var pirate = await Items(scope).CreateAsync(Input("Piraat", stock: 1));
        var party = await BundleAsync(scope, "Kinderfeest", jungle);
        var direct = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day.AddDays(1), "Anna");
        var viaBundle = await ReserveAsync(scope, new ReservationLineRequest(null, party, 1), day.AddDays(1), day.AddDays(2), "Bert");
        await ReserveAsync(scope, new ReservationLineRequest(pirate, null, 1), day, day);
        Assert.Empty(await Shortages(scope).GetAsync());
        Assert.Equal(0, await Shortages(scope).CountAsync());

        await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 1));

        var item = Assert.Single(await Shortages(scope).GetAsync());
        Assert.Equal((jungle, "Jungle"), (item.RentalItemId, item.RentalItemName));
        var shortageDay = Assert.Single(item.Days);
        Assert.Equal((day.AddDays(1), 1, 2, 1), (shortageDay.Day, shortageDay.Rentable, shortageDay.Reserved, shortageDay.Missing));
        Assert.Equal([direct.Number, viaBundle.Number], item.Reservations.Select(r => r.Number));
        var first = item.Reservations[0];
        Assert.Equal((direct.Id, "Anna Klant", new DateRange(day, day.AddDays(1)), 1, (string?)null),
            (first.ReservationId, first.CustomerName, first.Period, first.Quantity, first.BundleName));
        Assert.Equal([day.AddDays(1)], first.ShortageDays);
        Assert.Equal("Kinderfeest", item.Reservations[1].BundleName);
        Assert.Equal(2, await Shortages(scope).CountAsync());
    }

    [Fact]
    public async Task A_shortage_disappears_after_cancelling_shortening_the_blockout_or_raising_the_stock()
    {
        var day = TestReservations.FutureDay();
        await using var scope = await NewTenantAsync("tekorten-opgelost");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 1));
        var reservation = await ReserveAsync(scope, new ReservationLineRequest(jungle, null, 1), day, day);

        await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 0));
        Assert.Single(await Shortages(scope).GetAsync());
        await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 1));
        Assert.Empty(await Shortages(scope).GetAsync());

        var blockout = await Blockouts(scope).CreateAsync(new(jungle, 1, day.AddDays(-3), null, "Kapot"));
        Assert.Single(await Shortages(scope).GetAsync());
        await Blockouts(scope).SaveAsync(blockout.Id, new(jungle, 1, day.AddDays(-3), day.AddDays(-1), "Hersteld"));
        Assert.Empty(await Shortages(scope).GetAsync());

        await Blockouts(scope).SaveAsync(blockout.Id, new(jungle, 1, day.AddDays(-3), null, "Weer kapot"));
        Assert.Single(await Shortages(scope).GetAsync());
        await Reservations(scope).ChangeStatusAsync(reservation.Id, ReservationStatus.Cancelled, ReservationActor.Website);
        Assert.Empty(await Shortages(scope).GetAsync());
        Assert.Equal(0, await Shortages(scope).CountAsync());
    }

    [Fact]
    public async Task Shortages_in_the_past_do_not_count()
    {
        await using var scope = await NewTenantAsync("tekorten-verleden");
        var jungle = await Items(scope).CreateAsync(Input("Jungle", stock: 1));
        var today = DateOnly.FromDateTime(DateTime.Today);
        // Een reservatie die al voorbij is, staat er nog, rechtstreeks in de database: reserveren in het verleden kan niet.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = db.Customers.Add(new() { FirstName = "Oud", LastName = "Klant", Email = "oud@example.test" }).Entity;
        db.Reservations.Add(new Reservation
        {
            Number = "OUD-1", CustomerId = customer.Id, StartDate = today.AddDays(-10), EndDate = today.AddDays(-9),
            Lines = [new ReservationLine { RentalItemId = jungle, Quantity = 1, Description = "Jungle" }],
        });
        await db.SaveChangesAsync();

        await Items(scope).SaveAsync(jungle, Input("Jungle", stock: 0));

        Assert.Empty(await Shortages(scope).GetAsync());
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Staff)]
    public async Task Administrator_and_staff_see_the_shortages_and_their_count_in_the_menu(UserRole role)
    {
        var client = await app.LoggedInClientAsync($"tekorten-{role}@hopsakee.test", role);
        Reservation reservation;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var castle = await Items(scope).CreateAsync(Input($"Tekortkasteel {role}", stock: 1, slug: $"tekortkasteel-{role}".ToLowerInvariant()));
            reservation = await ReserveAsync(scope, new ReservationLineRequest(castle, null, 1), TestReservations.FutureDay(), TestReservations.FutureDay());
            await Blockouts(scope).CreateAsync(new(castle, 1, TestReservations.FutureDay(), null, "Lek"));
        }

        var response = await client.GetAsync("/admin/tekorten");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Tekortkasteel {role}", html);
        Assert.Contains($"href=\"admin/reservaties?nummer={reservation.Number}\"", html);
        Assert.Contains("href=\"admin/tekorten\"", html);
        Assert.Matches("data-shortage-count=\"[1-9]", html);

        var reservations = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties?nummer={reservation.Number}"));
        Assert.Contains(reservation.Number, reservations);
        Assert.Contains("href=\"admin/tekorten\"", reservations);
    }

    private async Task<AsyncServiceScope> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        return await app.TenantScopeAsync(slug);
    }

    private static async Task<Guid> BundleAsync(AsyncServiceScope scope, string name, Guid itemId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle
        {
            Slug = name.ToLowerInvariant(), Pricing = new Pricing { DayPrice = 150 },
            Translations = [new Translation { Culture = "nl", Name = name }],
            Items = [new BundleItem { RentalItemId = itemId, Quantity = 1 }],
        };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    private static async Task<Reservation> ReserveAsync(AsyncServiceScope scope, ReservationLineRequest line, DateOnly start, DateOnly end, string firstName = "Test")
    {
        var result = await Reservations(scope).ReserveAsync(new ReservationRequest(
            new DateRange(start, end), [line],
            new ReservationCustomer(firstName, "Klant", $"{firstName.ToLowerInvariant()}@example.test", null, new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private static RentalItemInput Input(string name, int stock, string? slug = null) =>
        new([new RentalItemText("nl", name)], null, stock, 100, null, NoSpecs, slug);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IBlockoutAdministration Blockouts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBlockoutAdministration>();

    private static IReservationService Reservations(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IReservationService>();

    private static IShortageOverview Shortages(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IShortageOverview>();
}
