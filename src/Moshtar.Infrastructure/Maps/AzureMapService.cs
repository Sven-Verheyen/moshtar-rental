using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;

namespace Moshtar.Infrastructure.Maps;

public class MapOptions
{
    /// <summary>
    /// Sleutel van het Azure Maps-account (ADR 0006). Leeg: adressen worden opgezocht met de lokale vervanger
    /// en de planning toont geen kaart (ontwikkeling en tests).
    /// </summary>
    public string? AzureMapsKey { get; set; }

    /// <summary>
    /// Sleutel voor de kaart in de browser. Die is voor iedereen leesbaar, dus in Azure beperkt tot het tonen van
    /// kaarttegels (ADR 0006). Leeg: de planning toont geen kaart, ook als de server Azure Maps wel gebruikt.
    /// </summary>
    public string? AzureMapsBrowserKey { get; set; }
}

/// <summary>
/// Zoekt adressen op met de Geocoding-API van Azure Maps en berekent routes met Route Directions (snelste volgorde,
/// verkeer op het vertrekuur) en Route Matrix. Niet getest in CI: de tests gebruiken <see cref="LocalMapService"/>.
/// Route v1 (api-version 1.0) wordt eind maart 2028 uitgefaseerd; dan overstappen op Route 2025-01-01.
/// </summary>
internal sealed class AzureMapService(HttpClient http, IOptions<MapOptions> options) : IMapService
{
    public const string BaseAddress = "https://atlas.microsoft.com/";

    public async Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default)
    {
        var query = string.Join('&',
            "api-version=2023-06-01",
            "top=1",
            "addressLine=" + Uri.EscapeDataString(address.Street.Trim()),
            "postalCode=" + Uri.EscapeDataString(address.PostalCode.Trim()),
            "locality=" + Uri.EscapeDataString(address.City.Trim()),
            "countryRegion=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(address.Country) ? "BE" : address.Country.Trim()));
        using var request = new HttpRequestMessage(HttpMethod.Get, "geocode?" + query);
        request.Headers.Add("subscription-key", options.Value.AzureMapsKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FeatureCollection>(ct);
        return Point(result?.Features?.FirstOrDefault());
    }

    public async Task<Route> RouteAsync(GeoPoint start, IReadOnlyList<GeoPoint> stops, DateTimeOffset departure, bool bestOrder, CancellationToken ct = default)
    {
        if (stops.Count == 0) return new Route([], [TimeSpan.Zero]);
        // Vertrek en aankomst op hetzelfde adres; computeBestOrder schikt enkel de punten ertussen.
        var waypoints = string.Join(':', stops.Prepend(start).Append(start).Select(p => p.ToString()));
        var query = string.Join('&',
            "api-version=1.0",
            "query=" + waypoints,
            "computeBestOrder=" + (bestOrder ? "true" : "false"),
            "departAt=" + Uri.EscapeDataString(departure.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture)),
            "traffic=true",
            "routeType=fastest",
            "travelMode=car",
            "routeRepresentation=summaryOnly");
        using var request = new HttpRequestMessage(HttpMethod.Get, "route/directions/json?" + query);
        request.Headers.Add("subscription-key", options.Value.AzureMapsKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DirectionsResult>(ct);
        var legs = result?.Routes?.FirstOrDefault()?.Legs?.Select(l => TimeSpan.FromSeconds(l.Summary?.TravelTimeInSeconds ?? 0)).ToList();
        if (legs is null || legs.Count != stops.Count + 1)
            throw new HttpRequestException("Azure Maps gaf geen volledige route terug.");
        var order = Enumerable.Range(0, stops.Count).ToArray();
        if (bestOrder && result!.OptimizedWaypoints is { Count: > 0 } optimized)
            foreach (var waypoint in optimized)
                order[waypoint.OptimizedIndex] = waypoint.ProvidedIndex;
        return new Route(order, legs);
    }

    public async Task<TimeSpan[][]> TravelTimesAsync(IReadOnlyList<GeoPoint> origins, IReadOnlyList<GeoPoint> destinations, DateTimeOffset departure, CancellationToken ct = default)
    {
        var query = string.Join('&',
            "api-version=1.0",
            "departAt=" + Uri.EscapeDataString(departure.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture)),
            "traffic=true",
            "routeType=fastest",
            "travelMode=car");
        var body = new
        {
            origins = new { type = "MultiPoint", coordinates = origins.Select(p => new[] { p.Longitude, p.Latitude }) },
            destinations = new { type = "MultiPoint", coordinates = destinations.Select(p => new[] { p.Longitude, p.Latitude }) },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "route/matrix/sync/json?" + query) { Content = JsonContent.Create(body) };
        request.Headers.Add("subscription-key", options.Value.AzureMapsKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MatrixResult>(ct);
        var matrix = result?.Matrix;
        if (matrix is null || matrix.Count != origins.Count || matrix.Any(row => row.Count != destinations.Count))
            throw new HttpRequestException("Azure Maps gaf geen volledige matrix terug.");
        return matrix.Select(row => row.Select(cell =>
            cell.Response?.RouteSummary?.TravelTimeInSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : throw new HttpRequestException("Azure Maps vond geen route tussen twee punten.")).ToArray()).ToArray();
    }

    /// <summary>
    /// Het punt van het beste resultaat, als het het adres zelf is. Met UpHierarchy vond Azure Maps enkel iets
    /// ruimers (de straat, de postcode, de gemeente) en dat punt zou de chauffeur naar de verkeerde plek sturen.
    /// </summary>
    private static GeoPoint? Point(Feature? feature)
    {
        if (feature?.Geometry?.Coordinates is not [var longitude, var latitude, ..] || feature.Properties is not { } properties)
            return null;
        if (properties.MatchCodes?.Contains("UpHierarchy") == true || properties.Confidence == "Low")
            return null;
        return GeoPoint.IsValid(latitude, longitude) ? new GeoPoint(latitude, longitude) : null;
    }

    private sealed record DirectionsResult(
        [property: JsonPropertyName("routes")] IReadOnlyList<DirectionsRoute>? Routes,
        [property: JsonPropertyName("optimizedWaypoints")] IReadOnlyList<OptimizedWaypoint>? OptimizedWaypoints);

    private sealed record DirectionsRoute([property: JsonPropertyName("legs")] IReadOnlyList<DirectionsLeg>? Legs);

    private sealed record DirectionsLeg([property: JsonPropertyName("summary")] RouteSummary? Summary);

    private sealed record RouteSummary([property: JsonPropertyName("travelTimeInSeconds")] int? TravelTimeInSeconds);

    /// <summary>Enkel de tussenliggende punten: index 0 is de eerste stop, niet het vertrekpunt.</summary>
    private sealed record OptimizedWaypoint(
        [property: JsonPropertyName("providedIndex")] int ProvidedIndex,
        [property: JsonPropertyName("optimizedIndex")] int OptimizedIndex);

    private sealed record MatrixResult([property: JsonPropertyName("matrix")] IReadOnlyList<IReadOnlyList<MatrixCell>>? Matrix);

    private sealed record MatrixCell([property: JsonPropertyName("response")] MatrixResponse? Response);

    private sealed record MatrixResponse([property: JsonPropertyName("routeSummary")] RouteSummary? RouteSummary);

    private sealed record FeatureCollection([property: JsonPropertyName("features")] IReadOnlyList<Feature>? Features);

    private sealed record Feature(
        [property: JsonPropertyName("geometry")] Geometry? Geometry,
        [property: JsonPropertyName("properties")] FeatureProperties? Properties);

    /// <summary>GeoJSON: eerst de lengtegraad, dan de breedtegraad.</summary>
    private sealed record Geometry([property: JsonPropertyName("coordinates")] double[]? Coordinates);

    private sealed record FeatureProperties(
        [property: JsonPropertyName("confidence")] string? Confidence,
        [property: JsonPropertyName("matchCodes")] IReadOnlyList<string>? MatchCodes);
}
