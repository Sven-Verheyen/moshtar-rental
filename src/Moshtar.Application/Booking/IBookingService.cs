using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Booking;

public interface IBookingService
{
    /// <summary>Welke artikelen zijn niet (voldoende) beschikbaar voor deze vraag?</summary>
    Task<IReadOnlyList<StockShortage>> CheckAvailabilityAsync(DateRange period, IReadOnlyList<BookingLineRequest> lines, CancellationToken ct = default);

    /// <summary>
    /// Maakt een reservatie aan en bevestigt ze meteen als de voorraad het toelaat.
    /// Gelijktijdige boekingen voor dezelfde tenant worden na elkaar afgehandeld,
    /// zodat het laatste exemplaar nooit twee keer verhuurd wordt.
    /// </summary>
    Task<BookingResult> BookAsync(BookingRequest request, CancellationToken ct = default);
}

public record BookingLineRequest(Guid? RentalItemId, Guid? BundleId, int Quantity);

public record BookingCustomer(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Address Address);

public record BookingRequest(
    DateRange Period,
    IReadOnlyList<BookingLineRequest> Lines,
    BookingCustomer Customer,
    DeliveryMethod DeliveryMethod,
    Address? DeliveryAddress,
    string? Notes,
    string Culture);

public record BookingResult(Reservation? Reservation, IReadOnlyList<StockShortage> Shortages)
{
    public bool Succeeded => Reservation is not null;
}
