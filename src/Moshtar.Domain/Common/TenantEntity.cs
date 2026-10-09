namespace Moshtar.Domain.Common;

/// <summary>
/// Basis voor alle data die bij één verhuurder (tenant) hoort.
/// De TenantId wordt door de infrastructuur ingevuld en gefilterd.
/// </summary>
public abstract class TenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
}
