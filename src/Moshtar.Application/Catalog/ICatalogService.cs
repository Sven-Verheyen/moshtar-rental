namespace Moshtar.Application.Catalog;

/// <summary>Leesmodel van de catalogus voor de boekingswebsite, vertaald naar de gevraagde taal.</summary>
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
    string? Category,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    string? ImageUrl,
    IReadOnlyList<string> Contents);
