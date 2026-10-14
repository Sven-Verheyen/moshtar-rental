using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De pakketten beheren. Een pakket heeft één slug in alle talen, uniek onder de artikelen en pakketten samen; de oude
/// slugs worden per taal bewaard (<see cref="OldSlugs"/>), zodat het oude adres in elke taal doorstuurt.
/// </summary>
internal sealed class BundleAdministration(AppDbContext db, ITenantContext tenantContext, OldSlugs oldSlugs, PhotoAlbum album)
    : IBundleAdministration
{
    public async Task<IReadOnlyList<BundleSummary>> GetAllAsync(CancellationToken ct = default)
    {
        var bundles = await db.Bundles.AsNoTracking().ToListAsync(ct);
        var items = await ItemsAsync(ct);
        return bundles
            .Select(b => new BundleSummary(
                b.Id, Name(b.Translations, b.Slug), b.Slug, b.Pricing.DayPrice, b.Pricing.ExtraDayPrice, b.IsActive,
                InCatalogOrder(b, items).Select(x => $"{x.Quantity} × {(items.TryGetValue(x.RentalItemId, out var item) ? Name(item.Translations, item.Slug) : "?")}").ToList(),
                SwitchedOffItems(b, items), CatalogTexts.Missing(Tenant, b.Translations), b.IsFeatured))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<BundleDetails?> GetAsync(Guid bundleId, CancellationToken ct = default)
    {
        var bundle = await db.Bundles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bundleId, ct);
        if (bundle is null) return null;
        var items = await ItemsAsync(ct);
        return new BundleDetails(bundle.Id, bundle.IsActive, new BundleInput(
                CatalogTexts.ForEditing(Tenant, bundle.Translations),
                InCatalogOrder(bundle, items).Select(x => new BundleContent(x.RentalItemId, x.Quantity)).ToList(),
                bundle.Pricing.DayPrice, bundle.Pricing.ExtraDayPrice, bundle.Slug),
            PhotoAlbum.Keys(bundle.Photos), SwitchedOffItems(bundle, items));
    }

    public async Task<Guid> CreateAsync(BundleInput input, CancellationToken ct = default)
    {
        var (translations, slug) = Validate(input);
        var bundle = new Bundle();
        await ChangeAsync(async () =>
        {
            await CheckItemsAsync(input.Contents, ct);
            bundle.Slug = await SlugAsync(slug, translations, bundle.Id, ct);
            Apply(bundle, input, translations);
            db.Bundles.Add(bundle);
        }, ct);
        return bundle.Id;
    }

    public Task SaveAsync(Guid bundleId, BundleInput input, CancellationToken ct = default)
    {
        var (translations, slug) = Validate(input);
        return ChangeAsync(async () =>
        {
            var bundle = await FindAsync(bundleId, ct);
            await CheckItemsAsync(input.Contents, ct);
            bundle.Slug = await SlugAsync(slug, translations, bundle.Id, ct);
            Apply(bundle, input, translations);
        }, ct);
    }

    public Task SetActiveAsync(Guid bundleId, bool isActive, CancellationToken ct = default) =>
        ChangeAsync(async () => (await FindAsync(bundleId, ct)).IsActive = isActive, ct);

    public async Task AddPhotosAsync(Guid bundleId, IReadOnlyList<PhotoUpload> photos, CancellationToken ct = default)
    {
        if (photos.Count == 0) return;
        var present = (await db.Bundles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bundleId, ct))?.Photos.Count
            ?? throw Gone();
        IReadOnlyList<string> keys;
        try
        {
            keys = await album.UploadAsync(present, photos, ct);
        }
        catch (ImageRejectedException e)
        {
            throw new BundleException([e.Message]);
        }
        try
        {
            await ChangeAsync(async () =>
            {
                var bundle = await FindAsync(bundleId, ct);
                PhotoAlbum.CheckRoom(bundle.Photos.Count, keys.Count);
                PhotoAlbum.Append(bundle.Photos, keys);
            }, ct);
        }
        catch (Exception e)
        {
            await album.ForgetAsync(keys, CancellationToken.None);
            if (e is ImageRejectedException) throw new BundleException([e.Message]);
            throw;
        }
    }

    public Task ReorderPhotosAsync(Guid bundleId, IReadOnlyList<string> keys, CancellationToken ct = default) =>
        ChangeAsync(async () =>
        {
            if (!PhotoAlbum.Reorder((await FindAsync(bundleId, ct)).Photos, keys))
                throw new BundleException(["De foto's zijn intussen gewijzigd; laad de pagina opnieuw."]);
        }, ct);

    public async Task DeletePhotoAsync(Guid bundleId, string key, CancellationToken ct = default)
    {
        await ChangeAsync(async () =>
        {
            if (!PhotoAlbum.Remove((await FindAsync(bundleId, ct)).Photos, key))
                throw new BundleException(["Deze foto is al verwijderd; laad de pagina opnieuw."]);
        }, ct);
        await album.ForgetAsync([key], ct);
    }

    public async Task DeleteAsync(Guid bundleId, CancellationToken ct = default)
    {
        IReadOnlyList<string> photos = [];
        await ChangeAsync(async () =>
        {
            var bundle = await FindAsync(bundleId, ct);
            var name = Name(bundle.Translations, bundle.Slug);
            if (await db.Reservations.AnyAsync(r => r.Lines.Any(l => l.BundleId == bundleId), ct))
                throw new BundleException(
                    [$"Het pakket \"{name}\" werd al gereserveerd en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite en blijven de reservaties ongewijzigd."],
                    canSwitchOff: true);
            if (await db.Carts.AnyAsync(c => c.Lines.Any(l => l.BundleId == bundleId), ct))
                throw new BundleException(
                    [$"Het pakket \"{name}\" ligt in de winkelkar van een klant en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite."],
                    canSwitchOff: true);
            await oldSlugs.ForgetAsync(SlugKind.Bundle, bundleId, ct);
            photos = PhotoAlbum.Keys(bundle.Photos);
            db.Bundles.Remove(bundle);
        }, ct);
        await album.ForgetAsync(photos, ct);
    }

    /// <summary>
    /// Voert een wijziging uit als enige wijziging aan de slugs van de artikelen en pakketten van deze verhuurder
    /// (<see cref="CatalogLock"/>). Een slug die daardoor anders werd, stuurt voortaan door naar het nieuwe adres.
    /// </summary>
    private async Task ChangeAsync(Func<Task> change, CancellationToken ct)
    {
        // In het back office leeft deze service zo lang als het scherm open staat: altijd vers inlezen.
        db.ChangeTracker.Clear();
        await using var transaction = await CatalogLock.AcquireAsync(db, Tenant.Id, ct);
        var before = await SlugsAsync(ct);
        await change();
        await db.SaveChangesAsync(ct);
        await oldSlugs.RecordAsync(SlugKind.Bundle, before, await SlugsAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>De slug van elk pakket, in elke taal van de verhuurder: een pakket heeft dezelfde slug in alle talen.</summary>
    private async Task<IReadOnlyDictionary<(Guid TargetId, string Culture), string>> SlugsAsync(CancellationToken ct)
    {
        var bundles = await db.Bundles.AsNoTracking().Select(b => new { b.Id, b.Slug }).ToListAsync(ct);
        return bundles.SelectMany(b => Tenant.Cultures.Select(culture => (Key: (b.Id, culture), b.Slug))).ToDictionary(x => x.Key, x => x.Slug);
    }

    /// <summary>
    /// De slug die de beheerder gaf, als ze nog vrij is; zonder slug een vrije slug uit de naam in de standaardtaal.
    /// Artikelen en pakketten delen hun padwoord, dus ook hun slugs.
    /// </summary>
    private async Task<string> SlugAsync(string? given, List<Translation> translations, Guid bundleId, CancellationToken ct)
    {
        var taken = await db.Bundles.AsNoTracking().Where(b => b.Id != bundleId).Select(b => b.Slug)
            .Concat(db.RentalItems.AsNoTracking().Select(i => i.Slug))
            .ToListAsync(ct);
        if (given is not null)
        {
            if (taken.Contains(given))
                throw new BundleException([$"De slug \"{given}\" hoort al bij een ander artikel of pakket; kies een andere."]);
            return given;
        }
        var fromName = RentalItemSlug.From(translations.Single(t => t.Culture == Tenant.DefaultCulture).Name);
        if (RentalItemSlug.Problem(fromName) is { } problem)
            throw new BundleException([$"Uit de naam kan geen slug gemaakt worden; vul er zelf een in. {problem}"]);
        return RentalItemSlug.Unique(fromName, taken);
    }

    private async Task CheckItemsAsync(IReadOnlyList<BundleContent> contents, CancellationToken ct)
    {
        var ids = contents.Select(c => c.RentalItemId).ToList();
        if (await db.RentalItems.CountAsync(i => ids.Contains(i.Id), ct) != ids.Count)
            throw new BundleException(["Een artikel in dit pakket bestaat niet (meer); laad de pagina opnieuw."]);
    }

    private async Task<Bundle> FindAsync(Guid bundleId, CancellationToken ct) =>
        await db.Bundles.FirstOrDefaultAsync(b => b.Id == bundleId, ct) ?? throw Gone();

    private static BundleException Gone() => new(["Dit pakket bestaat niet (meer)."]);

    private async Task<Dictionary<Guid, RentalItem>> ItemsAsync(CancellationToken ct) =>
        await db.RentalItems.AsNoTracking().Include(i => i.Category).ToDictionaryAsync(i => i.Id, ct);

    /// <summary>De artikelen van het pakket in de volgorde van het aanbod: per categorie, dan zoals de verhuurder ze schikte.</summary>
    private static IEnumerable<BundleItem> InCatalogOrder(Bundle bundle, IReadOnlyDictionary<Guid, RentalItem> items) =>
        bundle.Items.OrderBy(x => items.GetValueOrDefault(x.RentalItemId) is { } item
            ? (item.Category is null, item.Category?.SortOrder ?? 0, item.SortOrder)
            : (true, int.MaxValue, int.MaxValue));

    /// <summary>De namen van de uitgezette artikelen in het pakket: zolang er een is, staat het niet op de reservatiewebsite.</summary>
    private List<string> SwitchedOffItems(Bundle bundle, IReadOnlyDictionary<Guid, RentalItem> items) =>
        bundle.Items.Select(x => items.GetValueOrDefault(x.RentalItemId))
            .Where(i => i is { IsActive: false })
            .Select(i => Name(i!.Translations, i.Slug))
            .ToList();

    private static void Apply(Bundle bundle, BundleInput input, List<Translation> translations)
    {
        bundle.Translations = translations;
        bundle.Pricing = new Pricing { DayPrice = input.DayPrice, ExtraDayPrice = input.ExtraDayPrice };
        // Ter plaatse aanpassen: een artikel dat erin blijft, houdt zijn rij.
        bundle.Items.RemoveAll(x => input.Contents.All(c => c.RentalItemId != x.RentalItemId));
        foreach (var content in input.Contents)
        {
            if (bundle.Items.FirstOrDefault(x => x.RentalItemId == content.RentalItemId) is { } present) present.Quantity = content.Quantity;
            else bundle.Items.Add(new BundleItem { RentalItemId = content.RentalItemId, Quantity = content.Quantity });
        }
    }

    /// <summary>Controleert wat de beheerder ingaf; geeft de vertalingen (alleen talen met een naam) en de gegeven slug terug.</summary>
    private (List<Translation> Translations, string? Slug) Validate(BundleInput input)
    {
        var problems = new List<string>();
        var translations = CatalogTexts.Validate(Tenant, input.Texts, problems);

        var slug = input.Slug?.Trim() is { Length: > 0 } s ? s : null;
        if (slug is not null && RentalItemSlug.Problem(slug) is { } slugProblem) problems.Add(slugProblem);

        if (input.Contents.Count == 0) problems.Add("Een pakket bevat minstens één artikel.");
        if (input.Contents.Any(c => c.Quantity < 1)) problems.Add("Het aantal van een artikel in het pakket is minstens 1.");
        if (input.Contents.DistinctBy(c => c.RentalItemId).Count() != input.Contents.Count)
            problems.Add("Een artikel staat twee keer in het pakket; pas het aantal aan in plaats van het opnieuw toe te voegen.");
        if (input.DayPrice < 0) problems.Add("De dagprijs kan niet negatief zijn.");
        if (input.ExtraDayPrice < 0) problems.Add("De prijs per extra dag kan niet negatief zijn.");

        if (problems.Count > 0) throw new BundleException(problems);
        return (translations, slug);
    }

    private string Name(IReadOnlyList<Translation> translations, string slug) =>
        translations.For(Tenant.DefaultCulture, Tenant.DefaultCulture)?.Name ?? slug;

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
