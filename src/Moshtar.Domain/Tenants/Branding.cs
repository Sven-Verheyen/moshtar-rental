using System.Globalization;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Tenants;

/// <summary>
/// De kleur uit de huisstijl van een verhuurder. Alleen een hexkleur is geldig; zo komt er nooit iets anders in de HTML
/// van een mail of de website. Logo en sfeerfoto staan in de afbeeldingenopslag (ADR 0005).
/// </summary>
public static partial class Branding
{
    /// <summary>De kleur in kleine letters, bv. #e91e63 of #f60, of null als er geen (geldige) kleur is.</summary>
    public static string? Color(string? color) =>
        color?.Trim() is { } c && HexColor().IsMatch(c) ? c.ToLowerInvariant() : null;

    /// <summary>Wat er mis is met een ingevulde kleur. Leeg is altijd in orde.</summary>
    public static IReadOnlyList<string> Problems(string? color) =>
        !string.IsNullOrWhiteSpace(color) && Color(color) is null ? ["De kleur moet een hexcode zijn, bv. #e91e63."] : [];

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

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
