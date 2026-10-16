using System.Text.RegularExpressions;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Customers;

/// <summary>
/// Het adres zoals de btw-databank het geeft: één vrije tekst, meestal de straat en dan de postcode met de gemeente op
/// een eigen regel ("Kerkstraat 1\n2000 Antwerpen"). Moshtar bewaart die tekst zoals hij is, en haalt er zo goed als het
/// kan een <see cref="Address"/> uit om het factuuradres in te vullen.
/// </summary>
public static partial class VatRegistryAddress
{
    /// <summary>
    /// Straat, postcode en gemeente uit de tekst, met het land van het btw-nummer; null als er geen postcode en gemeente
    /// na een straat te vinden zijn.
    /// </summary>
    public static Address? Parse(string? text, string vatCountry)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count == 1)
            parts = parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (parts.Count < 2 || PostalCodeAndCity().Match(parts[^1]) is not { Success: true } last) return null;
        return new Address
        {
            Street = string.Join(", ", parts[..^1]),
            PostalCode = last.Groups["postal"].Value,
            City = last.Groups["city"].Value.Trim(),
            Country = CountryOf(vatCountry),
        };
    }

    /// <summary>Het land van een btw-landcode: EL is Griekenland (GR), XI is Noord-Ierland (GB).</summary>
    public static string CountryOf(string vatCountry) => vatCountry switch
    {
        "EL" => "GR",
        "XI" => "GB",
        _ => vatCountry,
    };

    // Belgisch 2000, Nederlands 1012 LG, Grieks 104 31 of 10431, Pools 00-950, Iers D02 X285 niet: dan geen adres.
    [GeneratedRegex(@"^(?<postal>\d{4}\s?[A-Z]{2}|[A-Z]{1,2}-?\d{3,5}|\d{2,3}[ -]?\d{2,3}|\d{4,6})\s+(?<city>\D.*)$")]
    private static partial Regex PostalCodeAndCity();
}
