using Microsoft.AspNetCore.Components;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Tenancy;

namespace Moshtar.Web.Tenancy;

/// <summary>
/// Bepaalt de tenant aan de hand van de domeinnaam. Werkt zowel in gewone HTTP-requests
/// (via HttpContext) als in interactieve Blazor-circuits (via NavigationManager).
/// </summary>
internal sealed class HostTenantContext(IHttpContextAccessor httpContextAccessor, NavigationManager navigation, ITenantStore store) : ITenantContext
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
