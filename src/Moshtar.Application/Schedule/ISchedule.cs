using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De Planning van de huidige verhuurder: per dag de leveringen en ophalingen, en wie komt afhalen of terugbrengt.
/// Afvinken gebeurt met de gewone statuswijziging van een reservatie. Eén dag openen (<see cref="GetDayAsync"/>)
/// zoekt wel de punten op van leveradressen die er nog geen hebben, en van het bedrijfsadres.
/// </summary>
public interface ISchedule
{
    /// <summary>Wat er op deze dag vertrekt en terugkomt.</summary>
    Task<ScheduleDay> GetDayAsync(DateOnly day, CancellationToken ct = default);

    /// <summary>
    /// Elke dag van <paramref name="from"/> tot en met <paramref name="to"/>, in volgorde en ook de lege, met dezelfde
    /// regels als <see cref="GetDayAsync"/>. Voor de week- en maandweergave.
    /// </summary>
    Task<IReadOnlyList<ScheduleDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// Eén dag van de Planning. <see cref="Deliveries"/> is de leverronde (reservaties die deze dag beginnen),
/// <see cref="Collections"/> de ophaalronde (die deze dag eindigen): in de volgorde van de bewaarde ronde
/// (<see cref="DeliveryRound"/>, <see cref="CollectionRound"/>), met wat er niet in zit op postcode erachter, of
/// helemaal op postcode zolang er geen route is. <see cref="Pickups"/> en <see cref="Returns"/> zijn de klanten die zelf komen afhalen of terugbrengen.
/// Een reservatie van één dag staat zowel bij de leveringen als bij de ophalingen. Geannuleerde reservaties ontbreken.
/// <see cref="BusinessPoint"/> is het bedrijfsadres van de verhuurder op de kaart, waar de rondes vertrekken; enkel
/// ingevuld bij <see cref="ISchedule.GetDayAsync"/>.
/// </summary>
public record ScheduleDay(
    DateOnly Day,
    IReadOnlyList<ScheduleStop> Deliveries,
    IReadOnlyList<ScheduleStop> Collections,
    IReadOnlyList<ScheduleStop> Pickups,
    IReadOnlyList<ScheduleStop> Returns,
    GeoPoint? BusinessPoint = null,
    ScheduleRound? DeliveryRound = null,
    ScheduleRound? CollectionRound = null)
{
    public bool IsEmpty => Deliveries.Count == 0 && Collections.Count == 0 && Pickups.Count == 0 && Returns.Count == 0;
}

/// <summary>
/// Een reservatie zoals ze in de Planning staat. <see cref="Address"/> is het leveradres, null bij afhalen;
/// <see cref="Point"/> het punt van dat adres op de kaart, null zolang het niet gevonden is. <see cref="InRound"/>
/// is de plaats en de uren in de bewaarde ronde, null als er geen route is of de stop er (nog) niet in zit.
/// </summary>
public record ScheduleStop(
    Guid ReservationId,
    string Number,
    string CustomerName,
    string? Phone,
    Address? Address,
    IReadOnlyList<ScheduleLine> Lines,
    string? Notes,
    ReservationStatus Status,
    DateRange Period,
    GeoPoint? Point = null,
    bool PointConfirmed = false,
    ScheduleRoundStop? InRound = null)
{
    /// <summary>Afgerond: alles is gedaan, de stop staat grijs.</summary>
    public bool IsCompleted => Status == ReservationStatus.Completed;

    /// <summary>Een levering of ophaling waarvan de kaart het adres niet vindt: de planning waarschuwt.</summary>
    public bool AddressNotFound => Address is not null && Point is null;
}

/// <summary>
/// Een lijn van de reservatie met de Nederlandse naam. Bij een pakket staan de artikelen erin in
/// <see cref="Contents"/>, al vermenigvuldigd met het aantal pakketten.
/// </summary>
public record ScheduleLine(string Name, int Quantity, IReadOnlyList<ScheduleLine> Contents);

/// <summary>
/// Een bewaarde ronde (een route) zoals de Planning ze toont. <see cref="Changed"/>: er kwam sinds de route een
/// reservatie bij of er viel er een weg (of een leverpunt veranderde); "Ronde bijwerken" brengt de ronde weer in orde.
/// <see cref="DefaultHandlingMinutes"/> is de opbouw- (leverronde) of afbouwtijd (ophaalronde) uit de instellingen.
/// </summary>
public record ScheduleRound(TimeOnly Departure, TimeOnly Return, bool Changed, int DefaultHandlingMinutes);

/// <summary>
/// Een stop in een bewaarde ronde: de plaats (vanaf 1), de berekende aankomst en het verwacht tijdsvenster, het vaste
/// uur en de eigen opbouw- of afbouwtijd als de gebruiker die koos, en of de aankomst na het vaste uur valt.
/// <see cref="MailedWindow"/> en <see cref="MailedAt"/>: het venster dat de klant laatst gemaild kreeg, en wanneer.
/// </summary>
public record ScheduleRoundStop(
    int Position,
    TimeOnly Arrival,
    ExpectedWindow Window,
    TimeOnly? FixedTime,
    int? HandlingMinutes,
    bool Late,
    ExpectedWindow? MailedWindow = null,
    DateTimeOffset? MailedAt = null)
{
    /// <summary>De klant kreeg een mail, maar het venster veranderde sindsdien: opnieuw mailen stuurt hem het nieuwe.</summary>
    public bool WindowChangedSinceMail => MailedWindow is not null && MailedWindow != Window;
}
