using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>Een verhuurbaar artikel: springkasteel, spel, ...</summary>
public class RentalItem : TenantEntity
{
    public string Slug { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Aantal exemplaren dat de verhuurder bezit.</summary>
    public int Stock { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public Pricing Pricing { get; set; } = new();

    public int? LengthCm { get; set; }
    public int? WidthCm { get; set; }
    public int? HeightCm { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public int? MaxPersons { get; set; }
    public bool RequiresPower { get; set; }

    public List<Translation> Translations { get; set; } = [];
    public List<ItemImage> Images { get; set; } = [];
}

public class ItemImage
{
    public string Url { get; set; } = "";
    public int SortOrder { get; set; }
}
