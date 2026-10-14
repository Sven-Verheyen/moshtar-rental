using System.Globalization;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Website;

namespace Moshtar.Web.Site;

/// <summary>De taal en de pagina van een request op de reservatiewebsite, zoals ze uit de URL gelezen zijn.</summary>
public sealed record SiteRequest(string Culture, SiteRoute? Route);

/// <summary>
/// Leest de taal uit de URL (ADR 0004): de standaardtaal van de verhuurder zonder voorvoegsel, de andere talen met
/// /fr/ of /en/. Het voorvoegsel wordt de PathBase, zodat de pagina's zelf geen taal in hun route hebben.
/// Een URL in de verkeerde vorm (bv. /fr/huren/x of /nl/...) stuurt met een 301 door naar de juiste.
/// </summary>
internal static class SiteLanguage
{
    /// <summary>De talen van de reservatiewebsite. Een verhuurder biedt er een deel van aan.</summary>
    public static readonly string[] Cultures = ["nl", "fr", "en"];

    /// <summary>Het back office is Nederlandstalig en heeft nooit een taalvoorvoegsel.</summary>
    private const string BackOfficeCulture = "nl";
    private static readonly string[] BackOfficeSegments = ["admin", "_blazor"];

    public static IApplicationBuilder UseSiteLanguage(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var tenant = context.RequestServices.GetRequiredService<ITenantContext>().Tenant!;
            var request = context.Request;

            // Bij een heruitvoering (bv. de 404-pagina) staat het voorvoegsel al in de PathBase.
            var culture = request.PathBase.Value?.Trim('/') is { Length: > 0 } prefixed && tenant.Cultures.Contains(prefixed)
                ? prefixed
                : null;
            var path = (request.Path.Value ?? "").Trim('/');
            var first = path.Split('/')[0];

            if (culture is null && Cultures.Contains(first))
            {
                var rest = path[first.Length..].TrimStart('/');
                if (!tenant.Cultures.Contains(first) || BackOfficeSegments.Contains(rest.Split('/')[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (first == tenant.DefaultCulture)
                {
                    context.Response.Redirect("/" + rest + request.QueryString, permanent: true);
                    return;
                }
                // Bewust niet teruggezet na het request: een heruitvoering (404-pagina) leest de taal uit de PathBase.
                culture = first;
                request.PathBase = request.PathBase.Add("/" + culture);
                request.Path = "/" + rest;
                path = rest;
            }

            if (culture is null && BackOfficeSegments.Contains(first))
            {
                UseCulture(context, BackOfficeCulture, null);
                await next();
                return;
            }

            culture ??= tenant.DefaultCulture;
            var route = SiteRoute.Parse(path) ?? await CategoryRouteAsync(context, path, culture);
            if (route is not null && route.PathIn(culture) != path)
            {
                context.Response.Redirect(SiteLinks.Href(route, culture, tenant.DefaultCulture) + request.QueryString, permanent: true);
                return;
            }

            UseCulture(context, culture, route);
            await next();
        });

    /// <summary>
    /// Een pad van één woord kan een categorie zijn. Eerst de slug in de taal van de URL; een slug uit een andere taal
    /// of een oude slug in de taal van de URL wordt herkend en daarna doorgestuurd, net als de vaste padwoorden.
    /// </summary>
    private static async Task<CategoryRoute?> CategoryRouteAsync(HttpContext context, string path, string culture)
    {
        if (path.Contains('/') || CategorySlug.Problem(path) is not null) return null;
        var categories = await context.RequestServices.GetRequiredService<IWebsiteTexts>().GetCategorySlugsAsync(context.RequestAborted);
        var category = categories.FirstOrDefault(c => c.PerCulture.GetValueOrDefault(culture) == path)
            ?? categories.FirstOrDefault(c => c.PerCulture.Values.Contains(path));
        if (category is null
            && await context.RequestServices.GetRequiredService<IOldSlugs>().FindAsync(SlugKind.Category, culture, path, context.RequestAborted) is { } id)
            category = categories.FirstOrDefault(c => c.CategoryId == id);
        return category is null ? null : new CategoryRoute(category.CategoryId, category.PerCulture);
    }

    private static void UseCulture(HttpContext context, string culture, SiteRoute? route)
    {
        var info = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;
        context.Response.Headers.ContentLanguage = culture;
        context.Features.Set(new SiteRequest(culture, route));
    }
}
