using System.Net;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Mail;

/// <summary>
/// De kop bovenaan de HTML-versie van een klantmail, met het logo en de kleur van de verhuurder.
/// Die komt er automatisch bij en staat niet in het mailsjabloon (ADR 0003). Zonder logo en kleur is er geen kop.
/// </summary>
internal static class MailHeader
{
    /// <summary>Achtergrond van de kop als de verhuurder wel een logo maar geen (geldige) kleur heeft.</summary>
    public const string NeutralColor = "#f4f4f5";

    /// <param name="logoUrl">Het publieke adres van het logo in de afbeeldingenopslag, of null zonder logo.</param>
    public static string Html(Tenant tenant, string? logoUrl) => Html(tenant.Name, logoUrl, tenant.PrimaryColor);

    /// <summary>De kop met dit logo en deze kleur, ook voor het voorbeeld van een huisstijl die nog niet bewaard is.</summary>
    public static string Html(string name, string? logoUrl, string? color)
    {
        var logo = WebAddress(logoUrl);
        color = Branding.Color(color);
        if (logo is null && color is null) return "";

        var background = color ?? NeutralColor;
        var content = logo is not null
            ? $"<img src=\"{E(logo)}\" alt=\"{E(name)}\" style=\"display:inline-block;max-height:64px;max-width:240px;border:0\">"
            : $"<span style=\"font-size:20px;font-weight:bold;color:{Branding.TextColorOn(background)}\">{E(name)}</span>";
        return $"<div style=\"background-color:{background};padding:16px 24px;margin-bottom:16px;text-align:center\">{content}</div>";
    }

    /// <summary>Alleen een volledig webadres komt in de mail, nooit bv. javascript: of een relatief pad.</summary>
    private static string? WebAddress(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
