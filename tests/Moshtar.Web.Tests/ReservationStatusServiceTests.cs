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
        var id = await ReserveAsync("statussen", new DateOnly(2027, 5, 1));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await reservations.ChangeStatusAsync(id, ReservationStatus.Delivered, ReservationActor.Website);
        var cancelled = await reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.Website);

        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Cancelling_frees_the_stock_for_a_new_reservation()
    {
        var first = await ReserveAsync("vrijgeven", new DateOnly(2027, 6, 1));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(first, ReservationStatus.Cancelled, ReservationActor.Website);

        var second = await ReserveAsync("vrijgeven", new DateOnly(2027, 6, 1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Another_rental_company_cannot_change_the_reservation()
    {
        var id = await ReserveAsync("afgeschermd", new DateOnly(2027, 7, 1));
        await using var scope = await app.TenantScopeAsync("andere");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.Website));
    }

    private Task<Guid> ReserveAsync(string itemSlug, DateOnly day) => TestReservations.ReserveAsync(app, itemSlug, day);
}
