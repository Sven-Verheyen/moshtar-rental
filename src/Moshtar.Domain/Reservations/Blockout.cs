using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

/// <summary>
/// Periode waarin exemplaren van een artikel niet verhuurbaar zijn (kapot, onderhoud, herstelling). Zonder einddatum
/// loopt ze tot de verhuurder er een invult.
/// </summary>
public class Blockout : TenantEntity
{
    public Guid RentalItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateOnly StartDate { get; set; }

    /// <summary>De laatste geblokkeerde dag, of null: dan zijn alle dagen vanaf de begindatum geblokkeerd.</summary>
    public DateOnly? EndDate { get; set; }

    public string? Reason { get; set; }

    /// <summary>
    /// De geblokkeerde dagen. Een open einde loopt tot de laatste dag die een <see cref="DateOnly"/> kent, "tot ver in de
    /// toekomst"; blokkeringen krijgen nooit bufferdagen, dus die periode wordt niet verder uitgebreid.
    /// </summary>
    public DateRange Period => new(StartDate, EndDate ?? DateOnly.MaxValue);

    /// <summary>Wat deze blokkering bezet, voor de beschikbaarheids- en tekortberekening.</summary>
    public StockDemand Demand => new(RentalItemId, Quantity, Period);
}
