using System.Globalization;

namespace Moshtar.Domain.Common;

/// <summary>Een punt op de kaart: breedte- en lengtegraad in WGS 84, zoals Azure Maps en Waze ze gebruiken.</summary>
public sealed record GeoPoint(double Latitude, double Longitude)
{
    /// <summary>Ligt het punt op de aarde? Wat van buiten komt (een speld uit de browser) moet dat eerst bewijzen.</summary>
    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    /// <summary>"51.2194,4.4025": de schrijfwijze van Waze en van de kaart in de browser, altijd met een punt.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.######},{Longitude:0.######}");
}
