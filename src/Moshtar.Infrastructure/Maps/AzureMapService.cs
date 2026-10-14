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
}

/// <summary>
/// Zoekt adressen op met de Geocoding-API van Azure Maps. Niet getest in CI: de tests gebruiken <see cref="LocalMapService"/>.
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
