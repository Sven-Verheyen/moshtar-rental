using Moshtar.Domain.Tenants;

namespace Moshtar.Web.Site;

/// <summary>
/// De keuze van de bezoeker over Google Analytics, in een functionele cookie. Zonder meet-ID bij de verhuurder is er
/// niets te kiezen: geen balk en geen script. Met een meet-ID laadt het script pas na "Accepteren".
/// De balk is een gewoon formulier, zodat de reservatiewebsite ook hiervoor geen JavaScript nodig heeft.
/// </summary>
public static class AnalyticsConsent
{
    public const string CookieName = "moshtar-analytics";
    public const string ChoosePath = "/toestemming";
    public const string ChangePath = "/toestemming/wijzigen";

    private const string Accepted = "ja";
    private const string Declined = "nee";

    /// <summary>Zes maanden; daarna vragen we opnieuw.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    /// <summary>Het meet-ID als de verhuurder Google Analytics gebruikt, anders null.</summary>
    public static string? MeasurementId(Tenant? tenant) => BusinessDetails.GoogleAnalyticsId(tenant?.GoogleAnalyticsId);

    /// <summary>De balk tonen: de verhuurder gebruikt Google Analytics en de bezoeker koos nog niet.</summary>
    public static bool MustAsk(HttpContext? context, Tenant? tenant) =>
        MeasurementId(tenant) is not null && context is not null && Choice(context) is null;

    /// <summary>Google Analytics laden: de verhuurder gebruikt het en de bezoeker aanvaardde.</summary>
    public static bool Allowed(HttpContext? context, Tenant? tenant) =>
        MeasurementId(tenant) is not null && context is not null && Choice(context) == true;

    private static bool? Choice(HttpContext context) => context.Request.Cookies[CookieName] switch
    {
        Accepted => true,
        Declined => false,
        _ => null,
    };

    /// <summary>De pagina waar de bezoeker was, om er na de keuze naar terug te keren.</summary>
    public static string ReturnUrl(HttpContext context) =>
        context.Request.PathBase + context.Request.Path + context.Request.QueryString;

    public static IEndpointRouteBuilder MapAnalyticsConsent(this IEndpointRouteBuilder app)
    {
        // Geen antiforgery: de keuze verandert alleen deze cookie, en een vervalste keuze kan de bezoeker altijd wijzigen.
        app.MapPost(ChoosePath, (HttpContext context) =>
        {
            var form = context.Request.Form;
            var choice = form["keuze"].ToString() switch
            {
                Accepted => Accepted,
                Declined => Declined,
                _ => null,
            };
            if (choice is not null)
                context.Response.Cookies.Append(CookieName, choice, CookieOptions(context));
            return Results.Redirect(LocalOrHome(form["terug"]));
        }).DisableAntiforgery();

        app.MapGet(ChangePath, (HttpContext context, string? terug) =>
        {
            context.Response.Cookies.Delete(CookieName, CookieOptions(context));
            return Results.Redirect(LocalOrHome(terug));
        });
        return app;
    }

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        Path = "/",
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        MaxAge = Lifetime,
    };

    /// <summary>Alleen terug naar een pagina op deze site, nooit naar een ander domein.</summary>
    internal static string LocalOrHome(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) && !url.Contains("\r") && !url.Contains("\n")
            ? url
            : "/";
}
