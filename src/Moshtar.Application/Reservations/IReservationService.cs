using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Reservations;

public interface IReservationService
{
    /// <summary>Welke artikelen zijn niet (voldoende) beschikbaar voor deze vraag?</summary>
    Task<IReadOnlyList<StockShortage>> CheckAvailabilityAsync(DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct = default);

    /// <summary>
    /// De actieve artikelen en pakketten die in deze huurperiode niet één keer te reserveren zijn, voor de lijst
    /// "wat is vrij op deze datum" op de reservatiewebsite.
    /// </summary>
    Task<IReadOnlySet<Guid>> FindUnavailableAsync(DateRange period, CancellationToken ct = default);

    /// <summary>
    /// Maakt een reservatie aan en bevestigt ze meteen als de voorraad het toelaat.
    /// Gelijktijdige reservaties voor dezelfde tenant worden na elkaar afgehandeld,
    /// zodat het laatste exemplaar nooit twee keer verhuurd wordt.
    /// </summary>
    Task<ReservationResult> ReserveAsync(ReservationRequest request, ReservationActor actor, CancellationToken ct = default);

    /// <summary>
    /// Zet een reservatie in een nieuwe status, ook annuleren, en noteert dat in de historiek.
    /// Geeft de bijgewerkte reservatie terug.
    /// </summary>
    Task<Reservation> ChangeStatusAsync(Guid reservationId, ReservationStatus status, ReservationActor actor, CancellationToken ct = default);

    /// <summary>
    /// Verbetert het leveradres van een reservatie, zoekt het opnieuw op de kaart en noteert dat in de historiek.
    /// Wordt het nog altijd niet gevonden, dan blijft het punt leeg (en de waarschuwing in de planning staan).
    /// Geeft de bijgewerkte reservatie terug.
    /// </summary>
    Task<Reservation> CorrectDeliveryAddressAsync(Guid reservationId, Address address, ReservationActor actor, CancellationToken ct = default);

    /// <summary>
    /// Zet het punt van het leveradres op deze speld, voor een adres dat zeker juist is maar dat de kaart niet kent,
    /// en noteert dat in de historiek. Geeft de bijgewerkte reservatie terug.
    /// </summary>
    Task<Reservation> ConfirmDeliveryPointAsync(Guid reservationId, GeoPoint point, ReservationActor actor, CancellationToken ct = default);
    /// Vervangt een artikel in de reservatie door een alternatief: een ander artikel uit dezelfde categorie dat aan staat
    /// en vrij is in de volledige huurperiode (met bufferdagen), gecontroleerd en bewaard onder dezelfde lock per
    /// verhuurder als reserveren. Op een losse lijn blijft de prijs dezelfde; in een pakket wordt enkel het artikel
    /// binnen het pakket vervangen, met een notitie op de pakketlijn. Een meerprijs (null of 0: geen) komt als aparte
    /// lijn bij het totaal. Noteert het in de historiek en stuurt de klant een bijgewerkte bevestiging.
    /// Gooit een <see cref="ArgumentException"/> als het alternatief niet kan, een <see cref="InvalidOperationException"/>
    /// als de reservatie geannuleerd of afgerond is; dan verandert er niets.
    /// </summary>
    Task<Reservation> ReplaceItemAsync(Guid reservationId, Guid rentalItemId, Guid replacementRentalItemId, decimal? surcharge,
        ReservationActor actor, CancellationToken ct = default);

    /// <summary>De historiek van een reservatie, nieuwste bovenaan.</summary>
    Task<IReadOnlyList<ReservationHistoryEntry>> GetHistoryAsync(Guid reservationId, CancellationToken ct = default);
}

/// <summary>
/// Een gebeurtenis uit de historiek. Zonder e-mailadres deed de klant het zelf via de reservatiewebsite.
/// <see cref="Note"/> zegt wat er precies gebeurde, bv. welke alternatieven gemaild werden, of is null.
/// </summary>
public record ReservationHistoryEntry(DateTime OccurredAtUtc, ReservationEventKind Kind, ReservationStatus Status, string? UserEmail, string? Note = null);

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
