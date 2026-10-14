using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>
/// Pakket van meerdere artikelen met een eigen prijs (bv. "Kinderfeest XL"). Het staat op de reservatiewebsite als het
/// zelf aan staat én al zijn artikelen aan staan; zijn beschikbaarheid volgt uit die van zijn artikelen.
/// </summary>
public class Bundle : TenantEntity
{
    public string Slug { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Pricing Pricing { get; set; } = new();
    public List<Translation> Translations { get; set; } = [];
    public List<BundleItem> Items { get; set; } = [];

    /// <summary>De eigen foto's, de hoofdfoto eerst (zie <see cref="Photo"/>). Zonder eigen foto's toont de reservatiewebsite die van de artikelen.</summary>
    public List<Photo> Photos { get; set; } = [];
}

public class BundleItem
{
    public Guid RentalItemId { get; set; }
    public int Quantity { get; set; } = 1;
}
