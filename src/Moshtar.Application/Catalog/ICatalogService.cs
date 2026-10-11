using Moshtar.Domain.Catalog;

namespace Moshtar.Application.Catalog;

/// <summary>Leesmodel van de catalogus voor de reservatiewebsite, vertaald naar de gevraagde taal.</summary>
public interface ICatalogService
{
    Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(string culture, CancellationToken ct = default);
    Task<CatalogEntry?> GetBySlugAsync(string slug, string culture, CancellationToken ct = default);
}

public enum CatalogEntryKind { Item, Bundle }

public record CatalogEntry(
    Guid Id,
    CatalogEntryKind Kind,
    string Slug,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? Category,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    IReadOnlyList<string> Images,
    IReadOnlyList<string> Contents,
    ItemSpecs? Specs = null)
{
    /// <summary>De eerste foto, voor de kaart in een lijst.</summary>
    public string? ImageUrl => Images.Count > 0 ? Images[0] : null;

    /// <summary>De prijs voor een huurperiode van zoveel dagen, met de bestaande prijsberekening.</summary>
    public decimal PriceFor(int days) => new Pricing { DayPrice = DayPrice, ExtraDayPrice = ExtraDayPrice }.PriceFor(days);
}

/// <summary>Wat een klant over een artikel wil weten voor hij reserveert. Pakketten hebben er geen.</summary>
public record ItemSpecs(int? LengthCm, int? WidthCm, int? HeightCm, int? MinAge, int? MaxAge, int? MaxPersons, bool RequiresPower)
{
    public bool HasDimensions => LengthCm is not null || WidthCm is not null || HeightCm is not null;
}
