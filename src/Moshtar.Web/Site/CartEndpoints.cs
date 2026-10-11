using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Moshtar.Application.Carts;
using Moshtar.Domain.Carts;

namespace Moshtar.Web.Site;

/// <summary>
/// De winkelkar wijzigen met gewone formulieren (POST), zodat de reservatiewebsite geen JavaScript nodig heeft.
/// Na elke wijziging gaat de klant terug naar de pagina waar hij was (POST, dan doorsturen), zodat herladen niets
/// opnieuw verstuurt. De winkelkar staat op de server; de klant heeft alleen de sleutel, in een functionele cookie.
/// </summary>
public static class CartEndpoints
{
    public const string CookieName = "moshtar-winkelkar";
    public const string AddPath = "/winkelkar/toevoegen";
    public const string QuantityPath = "/winkelkar/aantal";
    public const string PeriodPath = "/winkelkar/datum";
    public const string ContactPath = "/winkelkar/gegevens";

    /// <summary>De querystring-sleutel waarmee de winkelkarpagina stap 1 (het e-mailadres) opnieuw toont.</summary>
    public const string StepKey = "stap";
    public const string EmailStep = "email";

    /// <summary>De waarde van een aangevinkt vakje.</summary>
    public const string Checked = "ja";

    /// <summary>De querystring-sleutel waarmee de artikelpagina meldt hoe het toevoegen afliep.</summary>
    public const string ResultKey = "winkelkar";
    public const string Added = "toegevoegd";
    public const string NotAdded = "niet-vrij";

    /// <summary>De querystring-sleutel waarmee de winkelkarpagina een ongeldige datum meldt.</summary>
    public const string ProblemKey = "fout";


    public const string Item = "artikel";
    public const string Bundle = "pakket";

    /// <summary>De sleutel van de winkelkar van deze bezoeker, of null.</summary>
    public static string? Key(HttpContext? context) => context?.Request.Cookies[CookieName] is { Length: > 0 } key ? key : null;

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(AddPath, async (HttpContext context, IAntiforgery antiforgery, ICartService carts, TimeProvider clock) =>
        {
            var (valid, form) = await ReadAsync(context, antiforgery);
            var terug = AnalyticsConsent.LocalOrHome(form["terug"]);
            var period = ChosenPeriod.From(form[ChosenPeriod.StartKey], form[ChosenPeriod.EndKey], ChosenPeriod.Today(clock)).Period;
            if (!valid || Entry(form) is not { } entry || period is null)
                return Results.Redirect(terug);

            var change = await carts.AddAsync(Key(context), entry, Math.Max(1, Quantity(form, fallback: 1)), period.Value, context.RequestAborted);
            Remember(context, change);
            var separator = terug.Contains('?') ? '&' : '?';
            return Results.Redirect($"{terug}{separator}{ResultKey}={(change.Succeeded ? Added : NotAdded)}#reserveren");
        }).DisableAntiforgery();

        app.MapPost(QuantityPath, async (HttpContext context, IAntiforgery antiforgery, ICartService carts) =>
        {
            var (valid, form) = await ReadAsync(context, antiforgery);
            var terug = AnalyticsConsent.LocalOrHome(form["terug"]);
            if (valid && Entry(form) is { } entry)
                Remember(context, await carts.SetQuantityAsync(Key(context), entry, Quantity(form, fallback: 0), context.RequestAborted));
            return Results.Redirect(terug);
        }).DisableAntiforgery();

        app.MapPost(PeriodPath, async (HttpContext context, IAntiforgery antiforgery, ICartService carts, TimeProvider clock) =>
        {
            var (valid, form) = await ReadAsync(context, antiforgery);
            var terug = AnalyticsConsent.LocalOrHome(form["terug"]);
            if (!valid) return Results.Redirect(terug);
            var chosen = ChosenPeriod.From(form[ChosenPeriod.StartKey], form[ChosenPeriod.EndKey], ChosenPeriod.Today(clock));
            if (chosen.Period is not { } period)
                return Results.Redirect($"{terug}{(terug.Contains('?') ? '&' : '?')}{ProblemKey}={chosen.Problem ?? "PeriodUnreadable"}");
            Remember(context, await carts.ChangePeriodAsync(Key(context), period, context.RequestAborted));
            return Results.Redirect(terug);
        }).DisableAntiforgery();

        // Stap 1 van het reserveren: e-mailadres en de twee toestemmingen, meteen bewaard op de winkelkar.
        app.MapPost(ContactPath, async (HttpContext context, IAntiforgery antiforgery, ICartService carts) =>
        {
            var (valid, form) = await ReadAsync(context, antiforgery);
            var terug = AnalyticsConsent.LocalOrHome(form["terug"]);
            if (!valid) return Results.Redirect(terug);
            var email = form["email"].ToString().Trim();
            if (email.Length is 0 or > 320 || !new EmailAddressAttribute().IsValid(email))
                return Results.Redirect($"{terug}?{StepKey}={EmailStep}&{ProblemKey}=EmailInvalid#reserveren");
            var culture = SiteLanguage.Cultures.Contains(form["taal"].ToString()) ? form["taal"].ToString() : "nl";
            Remember(context, await carts.IdentifyAsync(Key(context), email, culture,
                wantsReminder: form["herinnering"] == Checked, wantsNews: form["nieuws"] == Checked, context.RequestAborted));
            return Results.Redirect(terug + "#reserveren");
        }).DisableAntiforgery();

        // De link in de herinneringsmail: opent de winkelkar, ook op een toestel zonder cookie, in de taal van de klant.
        app.MapGet(CartReminderPath.Route, async (string key, HttpContext context, ICartService carts, SiteLinks links) =>
        {
            var target = await carts.FindByReminderKeyAsync(key, context.RequestAborted);
            if (target is not null)
                Remember(context, new CartChange(target.Key, true));
            var culture = target?.Culture is { } c && SiteLanguage.Cultures.Contains(c) ? c : links.Culture;
            return Results.Redirect(links.Href(SiteRoute.Cart, culture));
        });
        return app;
    }

    /// <summary>Na het reserveren is de winkelkar afgesloten: de cookie mag weg.</summary>
    public static void Forget(HttpContext context)
    {
        if (!context.Response.HasStarted && Key(context) is not null)
            context.Response.Cookies.Delete(CookieName, CookieOptions(context));
    }

    /// <summary>
    /// Het antiforgery-token zelf nagaan, zoals bij de toestemming: een andere site kan niets in de winkelkar van de
    /// bezoeker leggen. Een ongeldig token verandert niets en stuurt gewoon terug.
    /// </summary>
    private static async Task<(bool Valid, IFormCollection Form)> ReadAsync(HttpContext context, IAntiforgery antiforgery)
    {
        var valid = await antiforgery.IsRequestValidAsync(context);
        return (valid, await context.Request.ReadFormAsync());
    }

    private static CatalogRef? Entry(IFormCollection form) =>
        Guid.TryParse(form["id"], out var id)
            ? form["soort"].ToString() switch
            {
                Item => CatalogRef.Item(id),
                Bundle => CatalogRef.Bundle(id),
                _ => null,
            }
            : null;

    private static int Quantity(IFormCollection form, int fallback) =>
        int.TryParse(form["aantal"], out var quantity) ? Math.Clamp(quantity, 0, Cart.MaxQuantity) : fallback;

    /// <summary>Bewaart de sleutel 7 dagen vanaf de laatste wijziging, of wist de cookie als er geen winkelkar meer is.</summary>
    private static void Remember(HttpContext context, CartChange change)
    {
        if (change.Key is { } key)
            context.Response.Cookies.Append(CookieName, key, CookieOptions(context));
        else if (Key(context) is not null)
            context.Response.Cookies.Delete(CookieName, CookieOptions(context));
    }

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        Path = "/",
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        // Een winkelkar die 7 dagen niet gewijzigd wordt, verdwijnt; de cookie ook.
        MaxAge = Cart.Lifetime,
    };
}
