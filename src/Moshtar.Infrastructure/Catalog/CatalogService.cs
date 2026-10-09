using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

internal sealed class CatalogService(AppDbContext db, ITenantContext tenantContext) : ICatalogService
{
    public async Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(string culture, CancellationToken ct = default)
    {
        var fallback = tenantContext.Tenant?.DefaultCulture ?? "nl";
        var items = await db.RentalItems.AsNoTracking().Include(i => i.Category)
            .Where(i => i.IsActive).ToListAsync(ct);
        var bundles = await db.Bundles.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        var itemNames = items.ToDictionary(i => i.Id, i => i.Translations.For(culture, fallback)?.Name ?? i.Slug);

        var entries = items
            .OrderBy(i => i.Category?.SortOrder).ThenBy(i => itemNames[i.Id])
            .Select(i =>
            {
                var t = i.Translations.For(culture, fallback);
                return new CatalogEntry(i.Id, CatalogEntryKind.Item, i.Slug, t?.Name ?? i.Slug, t?.Description,
                    i.Category?.Translations.For(culture, fallback)?.Name,
                    i.Pricing.DayPrice, i.Pricing.ExtraDayPrice,
                    i.Images.OrderBy(x => x.SortOrder).FirstOrDefault()?.Url, []);
            })
            .Concat(bundles.Select(b =>
            {
                var t = b.Translations.For(culture, fallback);
                var contents = b.Items.Select(x => $"{x.Quantity} × {itemNames.GetValueOrDefault(x.RentalItemId, "?")}").ToList();
                return new CatalogEntry(b.Id, CatalogEntryKind.Bundle, b.Slug, t?.Name ?? b.Slug, t?.Description,
                    null, b.Pricing.DayPrice, b.Pricing.ExtraDayPrice, null, contents);
            }));
        return entries.ToList();
    }

    public async Task<CatalogEntry?> GetBySlugAsync(string slug, string culture, CancellationToken ct = default) =>
        (await GetCatalogAsync(culture, ct)).FirstOrDefault(e => e.Slug == slug);
}
