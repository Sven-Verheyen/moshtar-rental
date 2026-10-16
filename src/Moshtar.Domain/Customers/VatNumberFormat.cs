using System.Text.RegularExpressions;

namespace Moshtar.Domain.Customers;

/// <summary>
/// Het formaat van een btw-nummer: de landcode en dan het nummer, zoals BE0123456789. Wordt gecontroleerd vóór een
/// nummer naar de btw-databank gaat, zodat een tikfout meteen opvalt. Een nummer uit een EU-land volgt het formaat van
/// dat land zoals de btw-databank het kent; een nummer van buiten de EU (een andere landcode) is niet te controleren
/// en wordt aanvaard zoals het is.
/// </summary>
public static partial class VatNumberFormat
{
    /// <summary>De nummers per EU-land (zonder landcode), zoals de btw-databank (VIES) ze kent. XI is Noord-Ierland.</summary>
    private static readonly Dictionary<string, Regex> EuFormats = new()
    {
        ["AT"] = new("^U[0-9]{8}$"),
        ["BE"] = new("^[01][0-9]{9}$"),
        ["BG"] = new("^[0-9]{9,10}$"),
        ["CY"] = new("^[0-9]{8}[A-Z]$"),
        ["CZ"] = new("^[0-9]{8,10}$"),
        ["DE"] = new("^[0-9]{9}$"),
        ["DK"] = new("^[0-9]{8}$"),
        ["EE"] = new("^[0-9]{9}$"),
        ["EL"] = new("^[0-9]{9}$"),
        ["ES"] = new("^[0-9A-Z][0-9]{7}[0-9A-Z]$"),
        ["FI"] = new("^[0-9]{8}$"),
        ["FR"] = new("^[0-9A-HJ-NP-Z]{2}[0-9]{9}$"),
        ["HR"] = new("^[0-9]{11}$"),
        ["HU"] = new("^[0-9]{8}$"),
        ["IE"] = new("^([0-9]{7}[A-W][A-IW]?|[0-9][A-Z+*][0-9]{5}[A-W])$"),
        ["IT"] = new("^[0-9]{11}$"),
        ["LT"] = new("^([0-9]{9}|[0-9]{12})$"),
        ["LU"] = new("^[0-9]{8}$"),
        ["LV"] = new("^[0-9]{11}$"),
        ["MT"] = new("^[0-9]{8}$"),
        ["NL"] = new("^[0-9]{9}B[0-9]{2}$"),
        ["PL"] = new("^[0-9]{10}$"),
        ["PT"] = new("^[0-9]{9}$"),
        ["RO"] = new("^[0-9]{2,10}$"),
        ["SE"] = new("^[0-9]{12}$"),
        ["SI"] = new("^[0-9]{8}$"),
        ["SK"] = new("^[0-9]{10}$"),
        ["XI"] = new("^([0-9]{9}|[0-9]{12}|GD[0-9]{3}|HA[0-9]{3})$"),
    };

    /// <summary>
    /// Het nummer zoals Moshtar het bewaart: in hoofdletters, zonder spaties, punten of koppeltekens, met EL voor
    /// Griekenland (ook als de klant GR tikte, zoals de btw-databank het wil). Null als er geen nummer is.
    /// </summary>
    public static string? Normalize(string? vatNumber)
    {
        if (string.IsNullOrWhiteSpace(vatNumber)) return null;
        var normalized = Separators().Replace(vatNumber, "").ToUpperInvariant();
        return normalized.StartsWith("GR") ? "EL" + normalized[2..] : normalized;
    }

    /// <summary>Begint het nummer met de landcode van een EU-land, zodat de btw-databank het kan controleren?</summary>
    public static bool IsEu(string vatNumber) =>
        Normalize(vatNumber) is { Length: > 2 } n && EuFormats.ContainsKey(n[..2]);

    /// <summary>
    /// Heeft het nummer een landcode en, voor een EU-land, het formaat van dat land? Een nummer van buiten de EU
    /// heeft alleen een landcode en een paar letters of cijfers nodig.
    /// </summary>
    public static bool IsWellFormed(string vatNumber)
    {
        if (Normalize(vatNumber) is not { } n || !WithCountryCode().IsMatch(n)) return false;
        return !EuFormats.TryGetValue(n[..2], out var format) || format.IsMatch(n[2..]);
    }

    [GeneratedRegex("[\\s.\\-/]")]
    private static partial Regex Separators();

    [GeneratedRegex("^[A-Z]{2}[0-9A-Z+*]{2,13}$")]
    private static partial Regex WithCountryCode();
}
