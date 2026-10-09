using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

/// <summary>Periode waarin exemplaren van een artikel niet verhuurbaar zijn (onderhoud, herstelling).</summary>
public class Blockout : TenantEntity
{
    public Guid RentalItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
}
