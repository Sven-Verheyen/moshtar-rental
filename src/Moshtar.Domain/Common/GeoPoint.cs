using System.Globalization;

namespace Moshtar.Domain.Common;

/// <summary>Een punt op de kaart: breedte- en lengtegraad in WGS 84, zoals Azure Maps en Waze ze gebruiken.</summary>
public sealed record GeoPoint(double Latitude, double Longitude)
{
    /// <summary>Ligt het punt op de aarde? Wat van buiten komt (een speld uit de browser) moet dat eerst bewijzen.</summary>
    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    /// <summary>
    /// Coördinaten zoals een gebruiker ze uit een kaart kopieert: "51.1702, 5.1703" (breedte, lengte), met een spatie
    /// of een puntkomma ertussen, of met decimale komma's als ze door een puntkomma gescheiden zijn. Null als het geen
    /// punt op de aarde is.
    /// </summary>
    public static GeoPoint? Parse(string? text)
    {
        var value = text?.Trim() ?? "";
        string[] parts = value.Contains(';')
            ? value.Split(';').Select(p => p.Replace(',', '.')).ToArray()
            : value.Split(',') is { Length: 2 } commas ? commas
            : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        return double.TryParse(parts[0], style, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(parts[1], style, CultureInfo.InvariantCulture, out var longitude)
            && IsValid(latitude, longitude)
                ? new GeoPoint(latitude, longitude)
                : null;
    }

    /// <summary>"51.2194,4.4025": de schrijfwijze van Waze en van de kaart in de browser, altijd met een punt.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.######},{Longitude:0.######}");
}
