using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Mail;

/// <summary>
/// De kop bovenaan de HTML-versie van een klantmail, met het logo en de kleur van de verhuurder.
/// Die komt er automatisch bij en staat niet in het mailsjabloon (ADR 0003). Zonder logo en kleur is er geen kop.
/// </summary>
internal static partial class MailHeader
{
    /// <summary>Achtergrond van de kop als de verhuurder wel een logo maar geen (geldige) kleur heeft.</summary>
    public const string NeutralColor = "#f4f4f5";

    public static string Html(Tenant tenant)
    {
        var logo = Logo(tenant.LogoUrl);
        var color = Color(tenant.PrimaryColor);
        if (logo is null && color is null) return "";

        var background = color ?? NeutralColor;
        var content = logo is not null
            ? $"<img src=\"{E(logo)}\" alt=\"{E(tenant.Name)}\" style=\"display:inline-block;max-height:64px;max-width:240px;border:0\">"
            : $"<span style=\"font-size:20px;font-weight:bold;color:{TextColorOn(background)}\">{E(tenant.Name)}</span>";
        return $"<div style=\"background-color:{background};padding:16px 24px;margin-bottom:16px;text-align:center\">{content}</div>";
    }

    /// <summary>Alleen een volledig http- of https-adres: een mailprogramma kan niets met een relatief pad.</summary>
    private static string? Logo(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    /// <summary>Alleen een hexkleur zoals #e91e63 of #f60; al de rest wordt genegeerd en komt nooit in de HTML.</summary>
    private static string? Color(string? color) =>
        color?.Trim() is { } c && HexColor().IsMatch(c) ? c.ToLowerInvariant() : null;

    /// <summary>Zwarte of witte tekst, naargelang wat het best leesbaar is op de achtergrond.</summary>
    private static string TextColorOn(string background)
    {
        var hex = background[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        double Channel(int i) => int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber) / 255.0;
        var luminance = 0.299 * Channel(0) + 0.587 * Channel(1) + 0.114 * Channel(2);
        return luminance > 0.6 ? "#000000" : "#ffffff";
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
