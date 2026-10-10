using Moshtar.Application.Tenancy;

namespace Moshtar.Web.Site;

/// <summary>Links naar pagina's van de reservatiewebsite, in de taal van het huidige request of in een andere taal.</summary>
public sealed class SiteLinks(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext)
{
    private HttpContext Http => httpContextAccessor.HttpContext ?? throw new InvalidOperationException("Geen request.");
    /// <summary>De standaardtaal van de verhuurder: die staat zonder voorvoegsel in de URL.</summary>
    public string DefaultCulture => tenantContext.Tenant?.DefaultCulture ?? "nl";

    /// <summary>De taal van de pagina, zoals die in de URL staat.</summary>
    public string Culture => Http.Features.Get<SiteRequest>()?.Culture ?? DefaultCulture;

    /// <summary>De pagina van het huidige request, of null als het geen pagina van de reservatiewebsite is.</summary>
    public SiteRoute? Current => Http.Features.Get<SiteRequest>()?.Route;

    /// <summary>De talen die de verhuurder aanbiedt.</summary>
    public IReadOnlyList<string> Cultures => tenantContext.Tenant?.Cultures ?? [DefaultCulture];

    /// <summary>Het pad naar deze pagina in de taal van de huidige pagina, bv. "/fr/louer/springkasteel".</summary>
    public string Href(SiteRoute route) => Href(route, Culture);

    public string Href(SiteRoute route, string culture) => Href(route, culture, DefaultCulture);

    /// <summary>De volledige URL op het hoofddomein, voor canonieke en hreflang-links.</summary>
    public string Absolute(SiteRoute route, string culture) =>
        $"https://{tenantContext.Tenant?.PrimaryHost ?? Http.Request.Host.Value}{Href(route, culture)}";

    internal static string Href(SiteRoute route, string culture, string defaultCulture)
    {
        var path = route.PathIn(culture);
        if (culture == defaultCulture) return "/" + path;
        return path.Length == 0 ? $"/{culture}" : $"/{culture}/{path}";
    }
}
