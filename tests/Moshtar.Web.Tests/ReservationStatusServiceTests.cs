using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>Reservaties opvolgen, wat Beheerders en Medewerkers allebei mogen.</summary>
public sealed class ReservationStatusServiceTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Reservation_moves_through_its_statuses_and_can_be_cancelled()
    {
        var id = await ReserveAsync("statussen", TestReservations.FutureDay(125));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await reservations.ChangeStatusAsync(id, ReservationStatus.Delivered, ReservationActor.Website);
        var cancelled = await reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.Website);

        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task A_reservation_from_before_the_phone_number_was_required_can_still_be_followed_up()
    {
        var id = await ReserveAsync("zonder-telefoon", TestReservations.FutureDay(126));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Zoals een reservatie van voor het telefoonnummer verplicht werd.
        var customerId = db.Reservations.Single(r => r.Id == id).CustomerId;
        await db.Customers.Where(c => c.Id == customerId).ExecuteUpdateAsync(c => c.SetProperty(x => x.Phone, (string?)null));
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        var delivered = await reservations.ChangeStatusAsync(id, ReservationStatus.Delivered, ReservationActor.Website);
        var returned = await reservations.ChangeStatusAsync(id, ReservationStatus.Returned, ReservationActor.Website);

        Assert.Equal((ReservationStatus.Delivered, ReservationStatus.Returned), (delivered.Status, returned.Status));
        Assert.Equal(3, (await reservations.GetHistoryAsync(id)).Count);
    }

    [Fact]
    public async Task Cancelling_frees_the_stock_for_a_new_reservation()
    {
        var first = await ReserveAsync("vrijgeven", TestReservations.FutureDay(156));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(first, ReservationStatus.Cancelled, ReservationActor.Website);

        var second = await ReserveAsync("vrijgeven", TestReservations.FutureDay(156));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Another_rental_company_cannot_change_the_reservation()
    {
        var id = await ReserveAsync("afgeschermd", TestReservations.FutureDay(187));
        await using var scope = await app.TenantScopeAsync("andere");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.Website));
    }

    private Task<Guid> ReserveAsync(string itemSlug, DateOnly day) => TestReservations.ReserveAsync(app, itemSlug, day);
}
