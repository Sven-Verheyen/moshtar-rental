using System.Text;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;

namespace Moshtar.Infrastructure.Maps;

/// <summary>
/// Kaartdienst zonder Azure Maps, lokaal en in de tests, zoals de lokale mail. Vindt elk adres met een Belgische
/// postcode (vier cijfers) en een huisnummer in de straat; een adres zonder huisnummer wordt niet gevonden. Het punt
/// volgt uit de postcode (ongeveer de juiste streek) en de straat, dus hetzelfde adres geeft altijd hetzelfde punt.
/// </summary>
public sealed class LocalMapService : IMapService
{
    /// <summary>Ongeveer het midden van de streek van elk eerste cijfer van een Belgische postcode.</summary>
    private static readonly (double Latitude, double Longitude)[] Regions =
    [
        (0, 0),
        (50.85, 4.35), // 1: Brussel en Brabant
        (51.20, 4.55), // 2: Antwerpen
        (50.93, 5.05), // 3: Leuven en Limburg
        (50.60, 5.60), // 4: Luik
        (50.40, 4.85), // 5: Namen
        (50.15, 5.00), // 6: Charleroi en Luxemburg
        (50.50, 3.90), // 7: Henegouwen
        (51.00, 3.05), // 8: West-Vlaanderen
        (51.05, 3.80), // 9: Oost-Vlaanderen
    ];

    public Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default)
    {
        var postalCode = address.PostalCode.Trim();
        var found = postalCode.Length == 4 && postalCode.All(char.IsAsciiDigit) && postalCode[0] != '0'
            && address.Street.Any(char.IsAsciiDigit);
        if (!found)
            return Task.FromResult<GeoPoint?>(null);

        var code = int.Parse(postalCode);
        var (latitude, longitude) = Regions[code / 1000];
        var street = Fraction(address.Street.Trim().ToLowerInvariant());
        return Task.FromResult<GeoPoint?>(new GeoPoint(
            Math.Round(latitude + (code % 1000 / 1000.0 - 0.5) * 0.3 + (street - 0.5) * 0.02, 6),
            Math.Round(longitude + (code * 7 % 1000 / 1000.0 - 0.5) * 0.4 + (Fraction(new string(address.Street.Reverse().ToArray())) - 0.5) * 0.02, 6)));
    }

    /// <summary>Een getal tussen 0 en 1 dat enkel van de tekst afhangt (FNV-1a), anders dan string.GetHashCode.</summary>
    private static double Fraction(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
            hash = (hash ^ b) * 16777619u;
        return hash % 10000 / 10000.0;
    }
}
