using Moshtar.Domain.Tenants;

namespace Moshtar.Application.Tenancy;

/// <summary>De tenant (verhuurder) van het huidige request.</summary>
public interface ITenantContext
{
    Tenant? Tenant { get; }
    Guid TenantId => Tenant?.Id ?? Guid.Empty;
}
