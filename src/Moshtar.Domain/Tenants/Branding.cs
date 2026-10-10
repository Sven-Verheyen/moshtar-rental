using System.Globalization;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Tenants;

/// <summary>
/// De huisstijl van een verhuurder: het adres van zijn logo, zijn kleur en het adres van zijn sfeerfoto. Alleen een
/// volledig https-adres en een hexkleur zijn geldig; zo komt er nooit iets anders in de HTML van een mail of de website.
/// </summary>
public static partial class Branding
{
    /// <summary>Het logo-adres zoals het gebruikt wordt, of null als er geen (geldig) logo is.</summary>
    public static string? LogoUrl(string? url) => HttpsImage(url);

    /// <summary>Het adres van de sfeerfoto zoals het gebruikt wordt, of null als er geen (geldige) sfeerfoto is.</summary>
    public static string? HeroImageUrl(string? url) => HttpsImage(url);

    /// <summary>De kleur in kleine letters, bv. #e91e63 of #f60, of null als er geen (geldige) kleur is.</summary>
    public static string? Color(string? color) =>
        color?.Trim() is { } c && HexColor().IsMatch(c) ? c.ToLowerInvariant() : null;

    /// <summary>Wat er mis is met een ingevuld logo-adres, kleur en sfeerfoto-adres. Leeg is altijd in orde.</summary>
    public static IReadOnlyList<string> Problems(string? logoUrl, string? color, string? heroImageUrl = null)
    {
        var problems = new List<string>();
        if (!string.IsNullOrWhiteSpace(logoUrl) && LogoUrl(logoUrl) is null)
            problems.Add("Het logo moet een volledig adres zijn dat begint met https://.");
        if (!string.IsNullOrWhiteSpace(color) && Color(color) is null)
            problems.Add("De kleur moet een hexcode zijn, bv. #e91e63.");
        if (!string.IsNullOrWhiteSpace(heroImageUrl) && HeroImageUrl(heroImageUrl) is null)
            problems.Add("De sfeerfoto moet een volledig adres zijn dat begint met https://.");
        return problems;
    }

    /// <summary>Zwarte of witte tekst op een (geldige) kleur: wat het meeste contrast geeft, volgens WCAG.</summary>
    public static string TextColorOn(string color)
    {
        var hex = color[1..];
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

    private static string? HttpsImage(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0
            ? uri.AbsoluteUri
            : null;

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
