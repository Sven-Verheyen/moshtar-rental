using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>Een verhuurbaar artikel: springkasteel, spel, ...</summary>
public class RentalItem : TenantEntity
{
    public string Slug { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>De plaats binnen de categorie, zoals de verhuurder ze versleept; nieuwe artikelen komen achteraan.</summary>
    public int SortOrder { get; set; }

    /// <summary>Aantal exemplaren dat de verhuurder bezit.</summary>
    public int Stock { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    /// <summary>Uitgelicht op de home, op plaats <see cref="FeaturedOrder"/> tussen de uitgelichte artikelen en pakketten samen.</summary>
    public bool IsFeatured { get; set; }
    public int FeaturedOrder { get; set; }
    public Pricing Pricing { get; set; } = new();

    public int? LengthCm { get; set; }
    public int? WidthCm { get; set; }
    public int? HeightCm { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public int? MaxPersons { get; set; }
    public bool RequiresPower { get; set; }

    public List<Translation> Translations { get; set; } = [];

    /// <summary>De foto's, de hoofdfoto eerst (zie <see cref="Photo"/>).</summary>
    public List<Photo> Photos { get; set; } = [];
}
