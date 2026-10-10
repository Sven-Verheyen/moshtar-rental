using System.Globalization;
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

    public static string Html(Tenant tenant) => Html(tenant.Name, tenant.LogoUrl, tenant.PrimaryColor);

    /// <summary>De kop met dit logo en deze kleur, ook voor het voorbeeld van een huisstijl die nog niet bewaard is.</summary>
    public static string Html(string name, string? logoUrl, string? color)
    {
        var logo = Branding.LogoUrl(logoUrl);
        color = Branding.Color(color);
        if (logo is null && color is null) return "";

        var background = color ?? NeutralColor;
        var content = logo is not null
            ? $"<img src=\"{E(logo)}\" alt=\"{E(name)}\" style=\"display:inline-block;max-height:64px;max-width:240px;border:0\">"
            : $"<span style=\"font-size:20px;font-weight:bold;color:{TextColorOn(background)}\">{E(name)}</span>";
        return $"<div style=\"background-color:{background};padding:16px 24px;margin-bottom:16px;text-align:center\">{content}</div>";
    }

    /// <summary>Zwarte of witte tekst: wat het meeste contrast geeft op de achtergrond, volgens WCAG.</summary>
    private static string TextColorOn(string background)
    {
        var hex = background[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        double Channel(int i)
        {
            var c = int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        var contrastWithBlack = (luminance + 0.05) / 0.05;
        return contrastWithBlack >= contrastWithWhite ? "#000000" : "#ffffff";
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
