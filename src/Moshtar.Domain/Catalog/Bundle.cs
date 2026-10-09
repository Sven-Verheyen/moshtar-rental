using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>Pakket van meerdere artikelen met een eigen prijs (bv. "Kinderfeest XL").</summary>
public class Bundle : TenantEntity
{
    public string Slug { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Pricing Pricing { get; set; } = new();
    public List<Translation> Translations { get; set; } = [];
    public List<BundleItem> Items { get; set; } = [];
}

public class BundleItem
{
    public Guid RentalItemId { get; set; }
    public int Quantity { get; set; } = 1;
}
