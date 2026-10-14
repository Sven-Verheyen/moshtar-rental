using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De Planning van de huidige verhuurder: per dag de leveringen en ophalingen, en wie komt afhalen of terugbrengt.
/// Afvinken gebeurt met de gewone statuswijziging van een reservatie. Een dag openen zoekt wel de punten op van
/// leveradressen die er nog geen hebben, en van het bedrijfsadres.
/// </summary>
public interface ISchedule
{
    /// <summary>Wat er op deze dag vertrekt en terugkomt.</summary>
    Task<ScheduleDay> GetDayAsync(DateOnly day, CancellationToken ct = default);
}

/// <summary>
/// Eén dag van de Planning. <see cref="Deliveries"/> is de leverronde (reservaties die deze dag beginnen),
/// <see cref="Collections"/> de ophaalronde (die deze dag eindigen), allebei op postcode gesorteerd zolang er geen
/// route is. <see cref="Pickups"/> en <see cref="Returns"/> zijn de klanten die zelf komen afhalen of terugbrengen.
/// Een reservatie van één dag staat zowel bij de leveringen als bij de ophalingen. Geannuleerde reservaties ontbreken.
/// <see cref="BusinessPoint"/> is het bedrijfsadres van de verhuurder op de kaart, waar de rondes vertrekken.
/// </summary>
public record ScheduleDay(
    DateOnly Day,
    IReadOnlyList<ScheduleStop> Deliveries,
    IReadOnlyList<ScheduleStop> Collections,
    IReadOnlyList<ScheduleStop> Pickups,
    IReadOnlyList<ScheduleStop> Returns,
    GeoPoint? BusinessPoint = null)
{
    public bool IsEmpty => Deliveries.Count == 0 && Collections.Count == 0 && Pickups.Count == 0 && Returns.Count == 0;
}

/// <summary>
/// Een reservatie zoals ze in de Planning staat. <see cref="Address"/> is het leveradres, null bij afhalen;
/// <see cref="Point"/> het punt van dat adres op de kaart, null zolang het niet gevonden is.
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
    bool PointConfirmed = false)
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
