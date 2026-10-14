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

        // In de volgorde van het aanbod: per categorie, dan zoals de verhuurder de artikelen schikte.
        var ordered = items
            .OrderBy(i => i.Category is null).ThenBy(i => i.Category?.SortOrder).ThenBy(i => i.SortOrder).ThenBy(i => itemNames[i.Id])
            .ToList();
        var position = ordered.Select((item, index) => (item.Id, index)).ToDictionary(x => x.Id, x => x.index);

        var entries = ordered
            .Select(i =>
            {
                var t = i.Translations.For(culture, fallback);
                // Zonder vertaalde beschrijving de beschrijving in de standaardtaal.
                var description = t?.Description ?? i.Translations.For(fallback, fallback)?.Description;
                return new CatalogEntry(i.Id, CatalogEntryKind.Item, i.Slug, t?.Name ?? i.Slug, description,
                    i.CategoryId, i.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                    i.Pricing.DayPrice, i.Pricing.ExtraDayPrice,
                    PhotoAlbum.Keys(i.Photos), [],
                    new ItemSpecs(i.LengthCm, i.WidthCm, i.HeightCm, i.MinAge, i.MaxAge, i.MaxPersons, i.RequiresPower),
                    InStock: i.Stock > 0, FeaturedOrder: i.IsFeatured ? i.FeaturedOrder : null);
            })
            // Een pakket staat op de reservatiewebsite zolang al zijn artikelen aan staan (hier: bij de actieve artikelen zijn).
            .Concat(bundles.Where(b => b.Items.All(x => itemsById.ContainsKey(x.RentalItemId))).Select(b =>
            {
                var t = b.Translations.For(culture, fallback);
                var description = t?.Description ?? b.Translations.For(fallback, fallback)?.Description;
                var contents = b.Items.OrderBy(x => position[x.RentalItemId]).ToList();
                // Zonder eigen foto's die van de artikelen erin. Het is te huren als elk artikel erin genoeg exemplaren heeft.
                var photos = b.Photos.Count > 0
                    ? PhotoAlbum.Keys(b.Photos)
                    : contents.SelectMany(x => PhotoAlbum.Keys(itemsById[x.RentalItemId].Photos)).ToList();
                var inStock = b.Items.All(x => itemsById[x.RentalItemId].Stock >= x.Quantity);
                return new CatalogEntry(b.Id, CatalogEntryKind.Bundle, b.Slug, t?.Name ?? b.Slug, description,
                    null, null, b.Pricing.DayPrice, b.Pricing.ExtraDayPrice, photos,
                    contents.Select(x => $"{x.Quantity} × {itemNames[x.RentalItemId]}").ToList(), InStock: inStock,
                    BundleItemIds: contents.Select(x => x.RentalItemId).ToList(),
                    FeaturedOrder: b.IsFeatured ? b.FeaturedOrder : null);
            }));
        return entries.ToList();
    }

    public async Task<CatalogEntry?> GetBySlugAsync(string slug, string culture, CancellationToken ct = default) =>
        (await GetCatalogAsync(culture, ct)).FirstOrDefault(e => e.Slug == slug);
}
