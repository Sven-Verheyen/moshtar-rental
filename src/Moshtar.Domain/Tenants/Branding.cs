using System.Text.RegularExpressions;

namespace Moshtar.Domain.Tenants;

/// <summary>
/// De huisstijl van een verhuurder: het adres van zijn logo en zijn kleur. Alleen een volledig https-adres
/// en een hexkleur zijn geldig; zo komt er nooit iets anders in de HTML van een mail.
/// </summary>
public static partial class Branding
{
    /// <summary>Het logo-adres zoals het gebruikt wordt, of null als er geen (geldig) logo is.</summary>
    public static string? LogoUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0
            ? uri.AbsoluteUri
            : null;

    /// <summary>De kleur in kleine letters, bv. #e91e63 of #f60, of null als er geen (geldige) kleur is.</summary>
    public static string? Color(string? color) =>
        color?.Trim() is { } c && HexColor().IsMatch(c) ? c.ToLowerInvariant() : null;

    /// <summary>Wat er mis is met een ingevuld logo-adres en een ingevulde kleur. Leeg is altijd in orde.</summary>
    public static IReadOnlyList<string> Problems(string? logoUrl, string? color)
    {
        var problems = new List<string>();
        if (!string.IsNullOrWhiteSpace(logoUrl) && LogoUrl(logoUrl) is null)
            problems.Add("Het logo moet een volledig adres zijn dat begint met https://.");
        if (!string.IsNullOrWhiteSpace(color) && Color(color) is null)
            problems.Add("De kleur moet een hexcode zijn, bv. #e91e63.");
        return problems;
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();
}
