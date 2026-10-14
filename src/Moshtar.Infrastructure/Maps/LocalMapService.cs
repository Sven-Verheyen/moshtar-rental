using System.Text;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;

namespace Moshtar.Infrastructure.Maps;

/// <summary>
/// Kaartdienst zonder Azure Maps, lokaal en in de tests, zoals de lokale mail. Vindt elk adres met een Belgische
/// postcode (vier cijfers) en een huisnummer in de straat; een adres zonder huisnummer wordt niet gevonden. Het punt
/// volgt uit de postcode (ongeveer de juiste streek) en de straat, dus hetzelfde adres geeft altijd hetzelfde punt.
/// Reistijden zijn de afstand in vogelvlucht aan <see cref="KilometersPerHour"/>, afgerond naar boven op hele minuten,
/// zonder verkeer; de snelste volgorde wordt voor een kleine ronde exact gezocht.
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

    /// <summary>De snelheid in vogelvlucht: wat trager dan over de weg, want de weg is langer dan de rechte lijn.</summary>
    public const double KilometersPerHour = 50;

    /// <summary>Tot zoveel stops worden alle volgordes geprobeerd; daarboven telkens de dichtstbije volgende stop.</summary>
    private const int ExactUpTo = 8;

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

    public Task<Route> RouteAsync(GeoPoint start, IReadOnlyList<GeoPoint> stops, DateTimeOffset departure, bool bestOrder, CancellationToken ct = default)
    {
        var order = !bestOrder ? Enumerable.Range(0, stops.Count).ToArray()
            : stops.Count <= ExactUpTo ? ExactOrder(start, stops)
            : NearestNeighbourOrder(start, stops);
        var points = order.Select(i => stops[i]).Prepend(start).Append(start).ToList();
        var legs = points.Zip(points.Skip(1), Travel).ToList();
        return Task.FromResult(new Route(order, legs));
    }

    public Task<TimeSpan[][]> TravelTimesAsync(IReadOnlyList<GeoPoint> origins, IReadOnlyList<GeoPoint> destinations, DateTimeOffset departure, CancellationToken ct = default) =>
        Task.FromResult(origins.Select(o => destinations.Select(d => Travel(o, d)).ToArray()).ToArray());

    /// <summary>De reistijd tussen twee punten: de afstand in vogelvlucht aan <see cref="KilometersPerHour"/>, in hele minuten.</summary>
    public static TimeSpan Travel(GeoPoint from, GeoPoint to) =>
        TimeSpan.FromMinutes(Math.Ceiling(Math.Round(Kilometers(from, to) / KilometersPerHour * 60, 6)));

    /// <summary>De afstand over het aardoppervlak (haversine), in kilometer.</summary>
    private static double Kilometers(GeoPoint a, GeoPoint b)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(b.Latitude - a.Latitude);
        var dLon = Radians(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(a.Latitude)) * Math.Cos(Radians(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Alle volgordes geprobeerd; bij gelijke reistijd de eerste in de gevraagde volgorde.</summary>
    private static int[] ExactOrder(GeoPoint start, IReadOnlyList<GeoPoint> stops)
    {
        int[]? best = null;
        var bestTime = TimeSpan.MaxValue;
        foreach (var order in Permutations(Enumerable.Range(0, stops.Count).ToList()))
        {
            var points = order.Select(i => stops[i]).Prepend(start).Append(start).ToList();
            var time = points.Zip(points.Skip(1), Travel).Aggregate(TimeSpan.Zero, (sum, leg) => sum + leg);
            if (time < bestTime) (best, bestTime) = (order, time);
        }
        return best ?? [];
    }

    private static IEnumerable<int[]> Permutations(List<int> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToArray();
            yield break;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }

    private static int[] NearestNeighbourOrder(GeoPoint start, IReadOnlyList<GeoPoint> stops)
    {
        var left = Enumerable.Range(0, stops.Count).ToList();
        var order = new List<int>(stops.Count);
        var here = start;
        while (left.Count > 0)
        {
            var next = left.MinBy(i => Kilometers(here, stops[i]));
            order.Add(next);
            left.Remove(next);
            here = stops[next];
        }
        return order.ToArray();
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
