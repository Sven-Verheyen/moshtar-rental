using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>Wat een slug aanduidt op de reservatiewebsite.</summary>
public enum SlugKind
{
    Category,
    RentalItem,
    Bundle,
}

/// <summary>
/// Een slug die een categorie, artikel of pakket vroeger had in één taal. Het oude adres stuurt met een 301 door naar
/// de huidige slug van <see cref="TargetId"/>, zodat links in zoekmachines en mails blijven werken. Krijgt iets
/// anders van dezelfde soort deze slug in deze taal, dan verdwijnt de oude slug: de huidige gaat voor.
/// </summary>
public class OldSlug : TenantEntity
{
    public SlugKind Kind { get; set; }
    public string Culture { get; set; } = "";
    public string Slug { get; set; } = "";
    /// <summary>De categorie, het artikel of het pakket waar de slug nu naar doorstuurt.</summary>
    public Guid TargetId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
