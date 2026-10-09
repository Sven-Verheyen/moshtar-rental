using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

internal static class TestReservations
{
    /// <summary>Reserveert het enige exemplaar van een artikel (aangemaakt bij eerste gebruik) op één dag.</summary>
    public static async Task<Guid> ReserveAsync(MoshtarApp app, string itemSlug, DateOnly day)
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
            DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
        return result.Reservation?.Id ?? throw new InvalidOperationException("Reserveren mislukt: " + string.Join(", ", result.Shortages));
    }
}
