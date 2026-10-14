using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De artikelen beheren. Een artikel heeft één slug in alle talen; de oude slugs worden per taal bewaard
/// (<see cref="OldSlugs"/>), zodat het oude adres in elke taal doorstuurt.
/// </summary>
internal sealed class RentalItemAdministration(
    AppDbContext db, ITenantContext tenantContext, OldSlugs oldSlugs, ShortageQuery shortages, PhotoAlbum album)
    : IRentalItemAdministration
{
    public async Task<IReadOnlyList<RentalItemSummary>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await db.RentalItems.AsNoTracking().Include(i => i.Category).ToListAsync(ct);
        return Ordered(items).Select(i => new RentalItemSummary(
            i.Id, Name(i), i.Slug, i.CategoryId, i.Stock, i.Pricing.DayPrice, i.Pricing.ExtraDayPrice, i.IsActive,
            CatalogTexts.Missing(Tenant, i.Translations), i.IsFeatured)).ToList();
    }

    public async Task<RentalItemDetails?> GetAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await db.RentalItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return null;
        var texts = CatalogTexts.ForEditing(Tenant, item.Translations);
        return new RentalItemDetails(item.Id, item.IsActive, new RentalItemInput(
            texts, item.CategoryId, item.Stock, item.Pricing.DayPrice, item.Pricing.ExtraDayPrice,
            new ItemSpecs(item.LengthCm, item.WidthCm, item.HeightCm, item.MinAge, item.MaxAge, item.MaxPersons, item.RequiresPower),
            item.Slug), PhotoAlbum.Keys(item.Photos));
    }

    public async Task<Guid> CreateAsync(RentalItemInput input, CancellationToken ct = default)
    {
        var (translations, slug) = Validate(input);
        var item = new RentalItem();
        await ChangeAsync(async () =>
        {
            await CheckCategoryAsync(input.CategoryId, ct);
            item.Slug = await SlugAsync(slug, translations, item.Id, ct);
            item.SortOrder = await NextSortOrderAsync(input.CategoryId, ct);
            Apply(item, input, translations);
            db.RentalItems.Add(item);
        }, ct);
        return item.Id;
    }

    public async Task<IReadOnlyList<Shortage>> SaveAsync(Guid itemId, RentalItemInput input, CancellationToken ct = default)
    {
        var (translations, slug) = Validate(input);
        var before = await shortages.FindAsync(itemId, ct);
        await ChangeAsync(async () =>
        {
            var item = await FindAsync(itemId, ct);
            await CheckCategoryAsync(input.CategoryId, ct);
            item.Slug = await SlugAsync(slug, translations, item.Id, ct);
            if (item.CategoryId != input.CategoryId) item.SortOrder = await NextSortOrderAsync(input.CategoryId, ct);
            Apply(item, input, translations);
        }, ct);
        return ShortageCalculator.NewOrWorse(before, await shortages.FindAsync(itemId, ct));
    }

    public Task ReorderAsync(Guid? categoryId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default) =>
        ChangeAsync(async () =>
        {
            var items = await db.RentalItems.Where(i => i.CategoryId == categoryId).ToListAsync(ct);
            if (itemIds.Count != items.Count || itemIds.Distinct().Count() != items.Count || !items.All(i => itemIds.Contains(i.Id)))
                throw new RentalItemException(["De artikelen zijn intussen gewijzigd; laad de pagina opnieuw."]);
            var order = itemIds.ToList();
            foreach (var item in items) item.SortOrder = order.IndexOf(item.Id) + 1;
        }, ct);

    public Task SetActiveAsync(Guid itemId, bool isActive, CancellationToken ct = default) =>
        ChangeAsync(async () => (await FindAsync(itemId, ct)).IsActive = isActive, ct);

    public async Task AddPhotosAsync(Guid itemId, IReadOnlyList<PhotoUpload> photos, CancellationToken ct = default)
    {
        if (photos.Count == 0) return;
        var present = (await db.RentalItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct))?.Photos.Count
            ?? throw new RentalItemException(["Dit artikel bestaat niet (meer)."]);
        IReadOnlyList<string> keys;
        try
        {
            keys = await album.UploadAsync(present, photos, ct);
        }
        catch (ImageRejectedException e)
        {
            throw new RentalItemException([e.Message]);
        }
        try
        {
            await ChangeAsync(async () =>
            {
                var item = await FindAsync(itemId, ct);
                PhotoAlbum.CheckRoom(item.Photos.Count, keys.Count);
                PhotoAlbum.Append(item.Photos, keys);
            }, ct);
        }
        catch (Exception e)
        {
            await album.ForgetAsync(keys, CancellationToken.None);
            if (e is ImageRejectedException) throw new RentalItemException([e.Message]);
            throw;
        }
    }

    public Task ReorderPhotosAsync(Guid itemId, IReadOnlyList<string> keys, CancellationToken ct = default) =>
        ChangeAsync(async () =>
        {
            if (!PhotoAlbum.Reorder((await FindAsync(itemId, ct)).Photos, keys))
                throw new RentalItemException(["De foto's zijn intussen gewijzigd; laad de pagina opnieuw."]);
        }, ct);

    public async Task DeletePhotoAsync(Guid itemId, string key, CancellationToken ct = default)
    {
        await ChangeAsync(async () =>
        {
            if (!PhotoAlbum.Remove((await FindAsync(itemId, ct)).Photos, key))
                throw new RentalItemException(["Deze foto is al verwijderd; laad de pagina opnieuw."]);
        }, ct);
        await album.ForgetAsync([key], ct);
    }

    public async Task DeleteAsync(Guid itemId, CancellationToken ct = default)
    {
        IReadOnlyList<string> photos = [];
        await ChangeAsync(async () =>
        {
            var item = await FindAsync(itemId, ct);
            var name = Name(item);
            var bundles = await db.Bundles.AsNoTracking().Where(b => b.Items.Any(x => x.RentalItemId == itemId)).ToListAsync(ct);
            var bundleIds = bundles.Select(b => b.Id).ToList();
            // Los, in een gereserveerd pakket of als vervangend artikel binnen een gereserveerd pakket.
            if (await db.Reservations.AnyAsync(r => r.Lines.Any(l => l.RentalItemId == itemId
                    || (l.BundleId != null && bundleIds.Contains(l.BundleId.Value))
                    || l.Replacements.Any(x => x.ReplacementRentalItemId == itemId || x.RentalItemId == itemId)), ct))
                throw new RentalItemException(
                    [$"Het artikel \"{name}\" werd al gereserveerd en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite en blijven de reservaties ongewijzigd."],
                    canSwitchOff: true);
            if (bundles.Count > 0)
            {
                var names = string.Join(", ", bundles.Select(b => $"\"{b.Translations.For(Tenant.DefaultCulture, Tenant.DefaultCulture)?.Name ?? b.Slug}\""));
                var inBundles = $"{(bundles.Count == 1 ? "pakket" : "de pakketten")} {names}";
                throw new RentalItemException([$"Het artikel \"{name}\" zit in {inBundles} en kan niet verwijderd worden. Haal het eerst uit {inBundles}."]);
            }
            if (await db.Carts.AnyAsync(c => c.Lines.Any(l => l.RentalItemId == itemId), ct))
                throw new RentalItemException(
                    [$"Het artikel \"{name}\" ligt in de winkelkar van een klant en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite."],
                    canSwitchOff: true);
            await oldSlugs.ForgetAsync(SlugKind.RentalItem, itemId, ct);
            photos = PhotoAlbum.Keys(item.Photos);
            db.RentalItems.Remove(item);
        }, ct);
        await album.ForgetAsync(photos, ct);
    }

    /// <summary>
    /// Voert een wijziging uit als enige wijziging aan de slugs van de artikelen en pakketten van deze verhuurder. Een
    /// slug die daardoor anders werd, stuurt voortaan door naar het nieuwe adres; een slug die weer in gebruik is, niet meer.
    /// </summary>
    private async Task ChangeAsync(Func<Task> change, CancellationToken ct)
    {
        // In het back office leeft deze service zo lang als het scherm open staat: altijd vers inlezen.
        db.ChangeTracker.Clear();
        await using var transaction = await CatalogLock.AcquireAsync(db, Tenant.Id, ct);
        var before = await SlugsAsync(ct);
        await change();
        await db.SaveChangesAsync(ct);
        await oldSlugs.RecordAsync(SlugKind.RentalItem, before, await SlugsAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>De slug van elk artikel, in elke taal van de verhuurder: een artikel heeft dezelfde slug in alle talen.</summary>
    private async Task<IReadOnlyDictionary<(Guid TargetId, string Culture), string>> SlugsAsync(CancellationToken ct)
    {
        var items = await db.RentalItems.AsNoTracking().Select(i => new { i.Id, i.Slug }).ToListAsync(ct);
        return items.SelectMany(i => Tenant.Cultures.Select(culture => (Key: (i.Id, culture), i.Slug))).ToDictionary(x => x.Key, x => x.Slug);
    }

    /// <summary>
    /// De slug die de beheerder gaf, als ze nog vrij is; zonder slug een vrije slug uit de naam in de standaardtaal.
    /// Artikelen en pakketten delen hun padwoord, dus ook hun slugs.
    /// </summary>
    private async Task<string> SlugAsync(string? given, List<Translation> translations, Guid itemId, CancellationToken ct)
    {
        var taken = await db.RentalItems.AsNoTracking().Where(i => i.Id != itemId).Select(i => i.Slug)
            .Concat(db.Bundles.AsNoTracking().Select(b => b.Slug))
            .ToListAsync(ct);
        if (given is not null)
        {
            if (taken.Contains(given))
                throw new RentalItemException([$"De slug \"{given}\" hoort al bij een ander artikel of pakket; kies een andere."]);
            return given;
        }
        var fromName = RentalItemSlug.From(translations.Single(t => t.Culture == Tenant.DefaultCulture).Name);
        if (RentalItemSlug.Problem(fromName) is { } problem)
            throw new RentalItemException([$"Uit de naam kan geen slug gemaakt worden; vul er zelf een in. {problem}"]);
        return RentalItemSlug.Unique(fromName, taken);
    }

    private async Task<int> NextSortOrderAsync(Guid? categoryId, CancellationToken ct) =>
        (await db.RentalItems.Where(i => i.CategoryId == categoryId).MaxAsync(i => (int?)i.SortOrder, ct) ?? 0) + 1;

    private async Task CheckCategoryAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is { } id && !await db.Categories.AnyAsync(c => c.Id == id, ct))
            throw new RentalItemException(["Deze categorie bestaat niet (meer)."]);
    }

    private async Task<RentalItem> FindAsync(Guid itemId, CancellationToken ct) =>
        await db.RentalItems.FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new RentalItemException(["Dit artikel bestaat niet (meer)."]);

    private static void Apply(RentalItem item, RentalItemInput input, List<Translation> translations)
    {
        item.CategoryId = input.CategoryId;
        item.Translations = translations;
        item.Stock = input.Stock;
        item.Pricing = new Pricing { DayPrice = input.DayPrice, ExtraDayPrice = input.ExtraDayPrice };
        (item.LengthCm, item.WidthCm, item.HeightCm) = (input.Specs.LengthCm, input.Specs.WidthCm, input.Specs.HeightCm);
        (item.MinAge, item.MaxAge, item.MaxPersons, item.RequiresPower) = (input.Specs.MinAge, input.Specs.MaxAge, input.Specs.MaxPersons, input.Specs.RequiresPower);
    }

    /// <summary>Controleert wat de beheerder ingaf; geeft de vertalingen (alleen talen met een naam) en de gegeven slug terug.</summary>
    private (List<Translation> Translations, string? Slug) Validate(RentalItemInput input)
    {
        var problems = new List<string>();
        var translations = CatalogTexts.Validate(Tenant, input.Texts, problems);

        var slug = input.Slug?.Trim() is { Length: > 0 } s ? s : null;
        if (slug is not null && RentalItemSlug.Problem(slug) is { } slugProblem) problems.Add(slugProblem);

        if (input.Stock < 0) problems.Add("De voorraad kan niet negatief zijn.");
        if (input.DayPrice < 0) problems.Add("De dagprijs kan niet negatief zijn.");
        if (input.ExtraDayPrice < 0) problems.Add("De prijs per extra dag kan niet negatief zijn.");
        var specs = input.Specs;
        if (specs.LengthCm <= 0 || specs.WidthCm <= 0 || specs.HeightCm <= 0) problems.Add("Afmetingen zijn groter dan 0 cm.");
        if (specs.MinAge < 0 || specs.MaxAge < 0) problems.Add("Een leeftijd kan niet negatief zijn.");
        if (specs.MinAge > specs.MaxAge) problems.Add("De minimumleeftijd is hoger dan de maximumleeftijd.");
        if (specs.MaxPersons <= 0) problems.Add("Het maximum aantal personen is minstens 1.");

        if (problems.Count > 0) throw new RentalItemException(problems);
        return (translations, slug);
    }

    /// <summary>Per categorie in de volgorde van de verhuurder, artikelen zonder categorie achteraan.</summary>
    private IEnumerable<RentalItem> Ordered(IEnumerable<RentalItem> items) =>
        items.OrderBy(i => i.Category is null).ThenBy(i => i.Category?.SortOrder).ThenBy(i => i.SortOrder).ThenBy(Name);

    private string Name(RentalItem item) => item.Translations.For(Tenant.DefaultCulture, Tenant.DefaultCulture)?.Name ?? item.Slug;

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
