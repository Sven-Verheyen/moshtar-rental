using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

internal sealed class CatalogService(AppDbContext db, ITenantContext tenantContext, IWebsiteTexts websiteTexts) : ICatalogService
{
    public async Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(string culture, CancellationToken ct = default)
    {
        var fallback = tenantContext.Tenant?.DefaultCulture ?? "nl";
        var items = await db.RentalItems.AsNoTracking().Include(i => i.Category)
            .Where(i => i.IsActive).ToListAsync(ct);
        var bundles = await db.Bundles.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        // De naam van een categorie kan de verhuurder aanpassen bij de websiteteksten.
        var categoryNames = (await websiteTexts.GetAsync(culture, ct)).Categories.ToDictionary(c => c.CategoryId, c => c.Name.Value);
        var itemsById = items.ToDictionary(i => i.Id);
        var itemNames = items.ToDictionary(i => i.Id, i => i.Translations.For(culture, fallback)?.Name ?? i.Slug);

        var entries = items
            .OrderBy(i => i.Category is null).ThenBy(i => i.Category?.SortOrder).ThenBy(i => i.SortOrder).ThenBy(i => itemNames[i.Id])
            .Select(i =>
            {
                var t = i.Translations.For(culture, fallback);
                // Zonder vertaalde beschrijving de beschrijving in de standaardtaal.
                var description = t?.Description ?? i.Translations.For(fallback, fallback)?.Description;
                return new CatalogEntry(i.Id, CatalogEntryKind.Item, i.Slug, t?.Name ?? i.Slug, description,
                    i.CategoryId, i.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                    i.Pricing.DayPrice, i.Pricing.ExtraDayPrice,
                    i.Images.OrderBy(x => x.SortOrder).Select(x => x.Url).ToList(), [],
                    new ItemSpecs(i.LengthCm, i.WidthCm, i.HeightCm, i.MinAge, i.MaxAge, i.MaxPersons, i.RequiresPower),
                    InStock: i.Stock > 0);
            })
            .Concat(bundles.Select(b =>
            {
                var t = b.Translations.For(culture, fallback);
                var contents = b.Items.Select(x => $"{x.Quantity} × {itemNames.GetValueOrDefault(x.RentalItemId, "?")}").ToList();
                // Een pakket heeft geen eigen foto's: dan die van de artikelen erin. Het is te huren als elk artikel
                // erin actief is en genoeg exemplaren heeft.
                var images = b.Items.SelectMany(x => itemsById.TryGetValue(x.RentalItemId, out var item)
                    ? item.Images.OrderBy(image => image.SortOrder).Select(image => image.Url) : []).ToList();
                var inStock = b.Items.All(x => itemsById.TryGetValue(x.RentalItemId, out var item) && item.Stock >= x.Quantity);
                return new CatalogEntry(b.Id, CatalogEntryKind.Bundle, b.Slug, t?.Name ?? b.Slug, t?.Description,
                    null, null, b.Pricing.DayPrice, b.Pricing.ExtraDayPrice, images, contents, InStock: inStock,
                    BundleItemIds: b.Items.Select(x => x.RentalItemId).ToList());
            }));
        return entries.ToList();
    }

    public async Task<CatalogEntry?> GetBySlugAsync(string slug, string culture, CancellationToken ct = default) =>
        (await GetCatalogAsync(culture, ct)).FirstOrDefault(e => e.Slug == slug);
}
