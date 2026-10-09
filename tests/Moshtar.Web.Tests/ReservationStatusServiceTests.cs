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

        await reservations.ChangeStatusAsync(id, ReservationStatus.Delivered);
        var cancelled = await reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled);

        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Cancelling_frees_the_stock_for_a_new_reservation()
    {
        var first = await ReserveAsync("vrijgeven", new DateOnly(2027, 6, 1));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(first, ReservationStatus.Cancelled);

        var second = await ReserveAsync("vrijgeven", new DateOnly(2027, 6, 1));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Another_rental_company_cannot_change_the_reservation()
    {
        var id = await ReserveAsync("afgeschermd", new DateOnly(2027, 7, 1));
        await using var scope = await app.TenantScopeAsync("andere");
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reservations.ChangeStatusAsync(id, ReservationStatus.Cancelled));
    }

    /// <summary>Reserveert het enige exemplaar van een artikel (aangemaakt bij eerste gebruik) op één dag.</summary>
    private async Task<Guid> ReserveAsync(string itemSlug, DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = db.RentalItems.SingleOrDefault(i => i.Slug == itemSlug);
        if (item is null)
        {
            item = new RentalItem { Slug = itemSlug, Stock = 1, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = itemSlug }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }

        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day),
            [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer("Test", "Klant", "klant@example.test", null, new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, "nl"));
        return result.Reservation?.Id ?? throw new InvalidOperationException("Reserveren mislukt: " + string.Join(", ", result.Shortages));
    }
}
