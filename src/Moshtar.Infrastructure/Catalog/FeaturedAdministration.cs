using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De uitgelichte artikelen en pakketten. Ze delen één volgorde over twee tabellen, dus wie ze wijzigt, doet dat als
/// enige bij de verhuurder, binnen een transactie met een PostgreSQL advisory lock.
/// </summary>
internal sealed class FeaturedAdministration(AppDbContext db, ITenantContext tenantContext) : IFeaturedAdministration
{
    public async Task<IReadOnlyList<FeaturedEntry>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await db.RentalItems.AsNoTracking().ToListAsync(ct);
        var active = items.Where(i => i.IsActive).Select(i => i.Id).ToHashSet();
        var bundles = await db.Bundles.AsNoTracking().Where(b => b.IsFeatured).ToListAsync(ct);
        return items.Where(i => i.IsFeatured)
            .Select(i => (Order: i.FeaturedOrder, Entry: new FeaturedEntry(i.Id, CatalogEntryKind.Item, Name(i.Translations, i.Slug), i.IsActive)))
            .Concat(bundles.Select(b => (Order: b.FeaturedOrder, Entry: new FeaturedEntry(b.Id, CatalogEntryKind.Bundle,
                Name(b.Translations, b.Slug), b.IsActive && b.Items.All(x => active.Contains(x.RentalItemId))))))
            .OrderBy(x => x.Order)
            .Select(x => x.Entry)
            .ToList();
    }

    public Task SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken ct = default) =>
        ChangeAsync((items, bundles) =>
        {
            var item = items.FirstOrDefault(i => i.Id == id);
            var bundle = bundles.FirstOrDefault(b => b.Id == id);
            if (item is null && bundle is null) throw new FeaturedException("Dit artikel of pakket bestaat niet (meer).");
            if ((item?.IsFeatured ?? bundle!.IsFeatured) == isFeatured) return;
            var last = items.Where(i => i.IsFeatured).Select(i => i.FeaturedOrder)
                .Concat(bundles.Where(b => b.IsFeatured).Select(b => b.FeaturedOrder))
                .DefaultIfEmpty(0).Max();
            if (item is not null) (item.IsFeatured, item.FeaturedOrder) = (isFeatured, isFeatured ? last + 1 : 0);
            else (bundle!.IsFeatured, bundle.FeaturedOrder) = (isFeatured, isFeatured ? last + 1 : 0);
        }, ct);

    public Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        ChangeAsync((items, bundles) =>
        {
            var featured = items.Where(i => i.IsFeatured).Select(i => i.Id).Concat(bundles.Where(b => b.IsFeatured).Select(b => b.Id)).ToHashSet();
            if (ids.Count != featured.Count || ids.Distinct().Count() != featured.Count || !ids.All(featured.Contains))
                throw new FeaturedException("De uitgelichte artikelen en pakketten zijn intussen gewijzigd; laad de pagina opnieuw.");
            var order = ids.ToList();
            foreach (var item in items.Where(i => i.IsFeatured)) item.FeaturedOrder = order.IndexOf(item.Id) + 1;
            foreach (var bundle in bundles.Where(b => b.IsFeatured)) bundle.FeaturedOrder = order.IndexOf(bundle.Id) + 1;
        }, ct);

    private async Task ChangeAsync(Action<List<RentalItem>, List<Bundle>> change, CancellationToken ct)
    {
        // In het back office leeft deze service zo lang als het scherm open staat: altijd vers inlezen.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = $"uitgelicht-{Tenant.Id}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        change(await db.RentalItems.ToListAsync(ct), await db.Bundles.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private string Name(IReadOnlyList<Translation> translations, string slug) =>
        translations.For(Tenant.DefaultCulture, Tenant.DefaultCulture)?.Name ?? slug;

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
