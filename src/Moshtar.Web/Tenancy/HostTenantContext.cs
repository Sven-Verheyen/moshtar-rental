using Microsoft.AspNetCore.Components;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Tenancy;

namespace Moshtar.Web.Tenancy;

/// <summary>
/// Bepaalt de tenant aan de hand van de domeinnaam. Werkt zowel in gewone HTTP-requests
/// (via HttpContext) als in interactieve Blazor-circuits (via NavigationManager).
/// </summary>
public sealed class HostTenantContext(IHttpContextAccessor httpContextAccessor, NavigationManager navigation, ITenantStore store) : ITenantContext
{
    private Tenant? _tenant;
    private bool _resolved;

    public Tenant? Tenant
    {
        get
        {
            if (_resolved) return _tenant;
            var host = ResolveHost();
            if (host is null) return null;
            _tenant = store.FindByHost(host);
            _resolved = true;
            return _tenant;
        }
    }

    /// <summary>Legt de tenant vast buiten een request om, bv. bij het aanmaken van de eerste Beheerder.</summary>
    public void UseTenant(Tenant tenant)
    {
        _tenant = tenant;
        _resolved = true;
    }

    private string? ResolveHost()
    {
        if (httpContextAccessor.HttpContext is { } http)
            return http.Request.Host.Host;
        try
        {
            return new Uri(navigation.BaseUri).Host;
        }
        catch (InvalidOperationException)
        {
            return null; // NavigationManager nog niet geïnitialiseerd
        }
    }
}
