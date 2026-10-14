using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De Planning van de huidige verhuurder: per dag de leveringen en ophalingen, en wie komt afhalen of terugbrengt.
/// Alleen lezen; afvinken gebeurt met de gewone statuswijziging van een reservatie.
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
/// <see cref="Collections"/> de ophaalronde (die deze dag eindigen), allebei op postcode gesorteerd zolang er geen
/// route is. <see cref="Pickups"/> en <see cref="Returns"/> zijn de klanten die zelf komen afhalen of terugbrengen.
/// Een reservatie van één dag staat zowel bij de leveringen als bij de ophalingen. Geannuleerde reservaties ontbreken.
/// </summary>
public record ScheduleDay(
    DateOnly Day,
    IReadOnlyList<ScheduleStop> Deliveries,
    IReadOnlyList<ScheduleStop> Collections,
    IReadOnlyList<ScheduleStop> Pickups,
    IReadOnlyList<ScheduleStop> Returns)
{
    public bool IsEmpty => Deliveries.Count == 0 && Collections.Count == 0 && Pickups.Count == 0 && Returns.Count == 0;
}

/// <summary>
/// Een reservatie zoals ze in de Planning staat. <see cref="Address"/> is het leveradres, null bij afhalen.
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
    DateRange Period)
{
    /// <summary>Afgerond: alles is gedaan, de stop staat grijs.</summary>
    public bool IsCompleted => Status == ReservationStatus.Completed;
}

/// <summary>
/// Een lijn van de reservatie met de Nederlandse naam. Bij een pakket staan de artikelen erin in
/// <see cref="Contents"/>, al vermenigvuldigd met het aantal pakketten.
/// </summary>
public record ScheduleLine(string Name, int Quantity, IReadOnlyList<ScheduleLine> Contents);
