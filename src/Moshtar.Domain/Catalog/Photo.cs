namespace Moshtar.Domain.Catalog;

/// <summary>
/// Een foto van een artikel (of pakket): de sleutel van de afbeelding in de afbeeldingenopslag (ADR 0005), en haar plaats.
/// De foto met de laagste <see cref="SortOrder"/> is de hoofdfoto.
/// </summary>
public class Photo
{
    /// <summary>Het maximum aantal foto's per artikel of pakket.</summary>
    public const int Max = 10;

    public string Key { get; set; } = "";
    public int SortOrder { get; set; }
}
