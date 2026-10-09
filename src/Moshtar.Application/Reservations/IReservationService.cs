using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Reservations;

public interface IReservationService
{
    /// <summary>Welke artikelen zijn niet (voldoende) beschikbaar voor deze vraag?</summary>
    Task<IReadOnlyList<StockShortage>> CheckAvailabilityAsync(DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct = default);

    /// <summary>
    /// Maakt een reservatie aan en bevestigt ze meteen als de voorraad het toelaat.
    /// Gelijktijdige reservaties voor dezelfde tenant worden na elkaar afgehandeld,
    /// zodat het laatste exemplaar nooit twee keer verhuurd wordt.
    /// </summary>
    Task<ReservationResult> ReserveAsync(ReservationRequest request, CancellationToken ct = default);

    /// <summary>Zet een reservatie in een nieuwe status, ook annuleren. Geeft de bijgewerkte reservatie terug.</summary>
    Task<Reservation> ChangeStatusAsync(Guid reservationId, ReservationStatus status, CancellationToken ct = default);
}

public record ReservationLineRequest(Guid? RentalItemId, Guid? BundleId, int Quantity);

public record ReservationCustomer(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Address Address);

public record ReservationRequest(
    DateRange Period,
    IReadOnlyList<ReservationLineRequest> Lines,
    ReservationCustomer Customer,
    DeliveryMethod DeliveryMethod,
    Address? DeliveryAddress,
    string? Notes,
    string Culture);

public record ReservationResult(Reservation? Reservation, IReadOnlyList<StockShortage> Shortages)
{
    public bool Succeeded => Reservation is not null;
}
