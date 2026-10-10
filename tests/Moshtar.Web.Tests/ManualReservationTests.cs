using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>Een gebruiker voert zelf een reservatie in, bijvoorbeeld voor een klant die telefonisch reserveert.</summary>
public sealed class ManualReservationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task User_reserves_items_and_bundles_and_is_named_in_the_history(UserRole role)
    {
        var email = $"invoer-{role}@hopsakee.test".ToLowerInvariant();
        var user = await app.CreateUserAsync("hopsakee", email, role);
        var (item, bundle) = await CreateItemAndBundleAsync($"telefoon-{role}".ToLowerInvariant(), stock: 3);

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        var result = await reservations.ReserveAsync(
            Request(TestReservations.FutureDay(249), new ReservationLineRequest(item, null, 1), new ReservationLineRequest(null, bundle, 1)),
            ReservationActor.User(user.Id));

        var reservation = Assert.IsType<Reservation>(result.Reservation);
        Assert.Matches(@"^\d{4}-\d{4}$", reservation.Number);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(2, reservation.Lines.Count);
        var created = Assert.Single(await reservations.GetHistoryAsync(reservation.Id));
        Assert.Equal((ReservationEventKind.Created, email), (created.Kind, created.UserEmail));
    }

    [Fact]
    public async Task Too_little_stock_is_refused_naming_the_item_and_the_day()
    {
        var user = await app.CreateUserAsync("hopsakee", "tekort@hopsakee.test", UserRole.Staff);
        var (item, _) = await CreateItemAndBundleAsync("tekort", stock: 1);
        var day = TestReservations.FutureDay(258);

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(
            Request(day, new ReservationLineRequest(item, null, 2)), ReservationActor.User(user.Id));

        Assert.False(result.Succeeded);
        var shortage = Assert.Single(result.Shortages);
        Assert.Equal((item, day, 1, 2), (shortage.RentalItemId, shortage.Day, shortage.Stock, shortage.Required));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Reservations.Any(r => r.Lines.Any(l => l.RentalItemId == item)));
    }

    [Fact]
    public async Task Two_simultaneous_reservations_never_rent_out_more_than_the_stock()
    {
        var anna = await app.CreateUserAsync("hopsakee", "tegelijk-anna@hopsakee.test", UserRole.Staff);
        var ben = await app.CreateUserAsync("hopsakee", "tegelijk-ben@hopsakee.test", UserRole.Staff);
        var (item, _) = await CreateItemAndBundleAsync("laatste-exemplaar", stock: 1);

        var results = await Task.WhenAll(
            ReserveAsync(item, anna.Id),
            ReserveAsync(item, ben.Id),
            ReserveAsync(item, anna.Id));

        Assert.Single(results, r => r.Succeeded);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task Both_roles_can_open_the_new_reservation_page_from_the_list(UserRole role)
    {
        var client = await app.LoggedInClientAsync($"nieuw-{role}@hopsakee.test".ToLowerInvariant(), role);

        var list = await client.GetStringAsync("/admin/reservaties");
        var page = await client.GetAsync("/admin/reservaties/nieuw");

        Assert.Contains("href=\"admin/reservaties/nieuw\"", list);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Nieuwe reservatie", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_visitor_cannot_open_the_new_reservation_page()
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync("/admin/reservaties/nieuw");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/inloggen", response.Headers.Location!.ToString());
    }

    private async Task<ReservationResult> ReserveAsync(Guid item, Guid userId)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(
            Request(TestReservations.FutureDay(268), new ReservationLineRequest(item, null, 1)), ReservationActor.User(userId));
    }

    private static ReservationRequest Request(DateOnly day, params ReservationLineRequest[] lines) => new(
        new DateRange(day, day),
        lines,
        new ReservationCustomer("Telefonische", "Klant", "telefoon@example.test", "0470 12 34 56", new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" }),
        DeliveryMethod.Delivery, null, null, "nl");

    /// <summary>Een artikel met de gegeven voorraad en een pakket met één exemplaar van een ander artikel.</summary>
    private async Task<(Guid Item, Guid Bundle)> CreateItemAndBundleAsync(string slug, int stock)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new RentalItem { Slug = slug, Stock = stock, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = slug }] };
        var inBundle = new RentalItem { Slug = slug + "-in-pakket", Stock = stock, Pricing = new Pricing { DayPrice = 50 }, Translations = [new Translation { Culture = "nl", Name = slug + " in pakket" }] };
        var bundle = new Bundle { Slug = slug + "-pakket", Pricing = new Pricing { DayPrice = 120 }, Translations = [new Translation { Culture = "nl", Name = slug + " pakket" }], Items = [new BundleItem { RentalItemId = inBundle.Id }] };
        db.AddRange(item, inBundle, bundle);
        await db.SaveChangesAsync();
        return (item.Id, bundle.Id);
    }
}
