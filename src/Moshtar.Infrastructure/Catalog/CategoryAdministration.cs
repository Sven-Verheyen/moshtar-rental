using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Website;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Website;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De categorieën beheren. De vertaalde naam bij het aanmaken staat op de categorie zelf; wat de beheerder daarna
/// aanpast, wordt net als vroeger bij de websiteteksten bewaard als aanpassing (<see cref="WebsiteTextCustomization"/>).
/// </summary>
internal sealed class CategoryAdministration(AppDbContext db, ITenantContext tenantContext, OldSlugs oldSlugs, TimeProvider clock)
    : ICategoryAdministration
{
    private const int MaxNameLength = 100;
    private const int MaxIntroLength = 2000;

    private static readonly WebsiteTextKey[] CategoryKeys = [WebsiteTextKey.CategoryName, WebsiteTextKey.CategorySlug, WebsiteTextKey.CategoryIntro];

    public async Task<IReadOnlyList<CategoryDetails>> GetAllAsync(CancellationToken ct = default)
    {
        var texts = await TextsAsync(ct);
        var counts = await db.RentalItems.AsNoTracking()
            .Where(i => i.CategoryId != null)
            .GroupBy(i => i.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Count, ct);
        return texts.Categories.Select(c => new CategoryDetails(
            c.Id,
            counts.GetValueOrDefault(c.Id),
            texts.PerCulture.ToDictionary(kv => kv.Key, kv => kv.Value.Single(t => t.CategoryId == c.Id)))).ToList();
    }

    public async Task<Guid> CreateAsync(IReadOnlyList<NewCategoryText> texts, Guid userId, CancellationToken ct = default)
    {
        var problems = new List<string>();
        foreach (var text in texts.Where(t => !Tenant.Cultures.Contains(t.Culture)))
            problems.Add($"Je biedt de taal '{text.Culture}' niet aan.");
        var given = Tenant.Cultures
            .Select(culture => texts.FirstOrDefault(t => t.Culture == culture) is { } t
                ? (Culture: culture, Name: t.Name.Trim(), Slug: (t.Slug ?? "").Trim())
                : (Culture: culture, Name: "", Slug: ""))
            .ToList();
        foreach (var (culture, name, _) in given)
        {
            if (culture == Tenant.DefaultCulture && name.Length == 0) problems.Add($"De naam in het {LanguageName(culture)} is leeg.");
            if (name.Length > MaxNameLength) problems.Add($"De naam in het {LanguageName(culture)} is langer dan {MaxNameLength} tekens.");
        }
        Throw(problems);

        // De slug in elke taal waarin de beheerder een naam of slug gaf; in een andere taal geldt de slug van de standaardtaal.
        var slugs = given
            .Where(g => g.Name.Length > 0 || g.Slug.Length > 0)
            .ToDictionary(g => g.Culture, g => g.Slug.Length > 0 ? g.Slug : CategorySlug.From(g.Name));
        foreach (var (culture, slug) in slugs)
            if (CategorySlug.Problem(slug) is { } problem) problems.Add(InLanguage(culture, problem));
        Throw(problems);

        var category = new Category
        {
            Translations = given.Where(g => g.Name.Length > 0).Select(g => new Translation { Culture = g.Culture, Name = g.Name }).ToList(),
        };
        await ChangeAsync(async current =>
        {
            foreach (var (culture, slug) in slugs)
                if (Duplicate(current, culture, category.Id, slug) is { } duplicate) problems.Add(InLanguage(culture, duplicate));
            Throw(problems);

            var existingSlugs = await db.Categories.Select(c => c.Slug).ToListAsync(ct);
            category.Slug = Unique(slugs[Tenant.DefaultCulture], existingSlugs);
            category.SortOrder = (await db.Categories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 1;
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);

            var standard = await TextsAsync(ct, withCustomizations: false);
            foreach (var (culture, slug) in slugs)
                await SetAsync(culture, WebsiteTextKey.CategorySlug, category.Id, slug, standard.For(culture, category.Id).Slug.Value, userId, ct);
        }, ct);
        return category.Id;
    }

    public async Task SaveAsync(Guid categoryId, string culture, string name, string slug, string intro, Guid userId, CancellationToken ct = default)
    {
        CheckCulture(culture);
        (name, slug, intro) = (name.Trim(), slug.Trim(), intro.Trim().ReplaceLineEndings("\n"));
        var problems = new List<string>();
        if (name.Length == 0) problems.Add("De naam is leeg.");
        else if (name.Length > MaxNameLength) problems.Add($"De naam is langer dan {MaxNameLength} tekens.");
        if (CategorySlug.Problem(slug) is { } slugProblem) problems.Add(slugProblem);
        if (intro.Length > MaxIntroLength) problems.Add($"De inleiding is langer dan {MaxIntroLength} tekens.");
        Throw(problems);

        await ChangeAsync(async current =>
        {
            CheckExists(current, categoryId);
            if (Duplicate(current, culture, categoryId, slug) is { } duplicate) throw new CategoryException([duplicate]);
            var standard = (await TextsAsync(ct, withCustomizations: false)).For(culture, categoryId);
            await SetAsync(culture, WebsiteTextKey.CategoryName, categoryId, name, standard.Name.Value, userId, ct);
            await SetAsync(culture, WebsiteTextKey.CategorySlug, categoryId, slug, standard.Slug.Value, userId, ct);
            await SetAsync(culture, WebsiteTextKey.CategoryIntro, categoryId, intro, standard.Intro.Value, userId, ct);
        }, ct);
    }

    public async Task ResetAsync(Guid categoryId, string culture, WebsiteTextKey key, CancellationToken ct = default)
    {
        if (!CategoryKeys.Contains(key))
            throw new ArgumentException("Alleen de naam, slug en inleiding van een categorie.", nameof(key));
        await ChangeAsync(async current =>
        {
            CheckExists(current, categoryId);
            if (key == WebsiteTextKey.CategorySlug)
            {
                var standard = (await TextsAsync(ct, withCustomizations: false)).For(culture, categoryId).Slug.Value;
                if (Duplicate(current, culture, categoryId, standard) is { } duplicate) throw new CategoryException([duplicate]);
            }
            await db.WebsiteTextCustomizations
                .Where(t => t.Culture == culture && t.Key == key && t.CategoryId == categoryId)
                .ExecuteDeleteAsync(ct);
        }, ct);
    }

    public Task ReorderAsync(IReadOnlyList<Guid> categoryIds, CancellationToken ct = default) =>
        ChangeAsync(async _ =>
        {
            var categories = await db.Categories.ToListAsync(ct);
            if (categoryIds.Count != categories.Count || !categories.All(c => categoryIds.Contains(c.Id)))
                throw new CategoryException(["De categorieën zijn intussen gewijzigd; laad de pagina opnieuw."]);
            foreach (var category in categories) category.SortOrder = categoryIds.ToList().IndexOf(category.Id) + 1;
        }, ct);

    public Task DeleteAsync(Guid categoryId, CancellationToken ct = default) =>
        ChangeAsync(async current =>
        {
            CheckExists(current, categoryId);
            var items = await db.RentalItems.CountAsync(i => i.CategoryId == categoryId, ct);
            if (items > 0)
            {
                var name = current.For(Tenant.DefaultCulture, categoryId).Name.Value;
                throw new CategoryException([items == 1
                    ? $"De categorie \"{name}\" bevat nog 1 artikel. Zet dat artikel eerst in een andere categorie of verwijder het."
                    : $"De categorie \"{name}\" bevat nog {items} artikelen. Zet die artikelen eerst in een andere categorie of verwijder ze."]);
            }
            await oldSlugs.ForgetAsync(SlugKind.Category, categoryId, ct);
            await db.Categories.Where(c => c.Id == categoryId).ExecuteDeleteAsync(ct);
        }, ct);

    /// <summary>
    /// Voert een wijziging uit als enige wijziging aan de categorieslugs van deze verhuurder, zodat twee beheerders niet
    /// tegelijk dezelfde slug aan twee categorieën geven (de databank kan dat niet afdwingen: een standaardslug staat
    /// niet in de databank). Een slug die daardoor anders werd, stuurt voortaan door naar het nieuwe adres.
    /// </summary>
    private async Task ChangeAsync(Func<AllTexts, Task> change, CancellationToken ct)
    {
        await using var transaction = await LockSlugsAsync(ct);
        var before = await TextsAsync(ct);
        await change(before);
        await db.SaveChangesAsync(ct);
        var after = await TextsAsync(ct);
        await oldSlugs.RecordAsync(SlugKind.Category, before.Slugs(), after.Slugs(), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<IDbContextTransaction> LockSlugsAsync(CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = $"categorie-slugs-{Tenant.Id}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        return transaction;
    }

    /// <summary>De categorieën in hun volgorde, met hun teksten in elke taal (of de standaardteksten, zonder aanpassingen).</summary>
    private async Task<AllTexts> TextsAsync(CancellationToken ct, bool withCustomizations = true)
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        var customizations = withCustomizations
            ? await db.WebsiteTextCustomizations.AsNoTracking().Where(t => t.CategoryId != null).ToListAsync(ct)
            : [];
        return new AllTexts(
            categories,
            Tenant.Cultures.ToDictionary(culture => culture, culture => CategoryTextResolver.Resolve(Tenant, culture, customizations, categories)));
    }

    private sealed record AllTexts(IReadOnlyList<Category> Categories, IReadOnlyDictionary<string, IReadOnlyList<CategoryTexts>> PerCulture)
    {
        public CategoryTexts For(string culture, Guid categoryId) => PerCulture[culture].Single(t => t.CategoryId == categoryId);

        public IReadOnlyDictionary<(Guid TargetId, string Culture), string> Slugs() =>
            PerCulture.SelectMany(kv => kv.Value.Select(t => (Key: (t.CategoryId, kv.Key), t.Slug.Value)))
                .ToDictionary(x => x.Key, x => x.Value);
    }

    private static void CheckExists(AllTexts current, Guid categoryId)
    {
        if (current.Categories.All(c => c.Id != categoryId))
            throw new CategoryException(["Deze categorie bestaat niet (meer)."]);
    }

    /// <summary>Een slug mag maar bij één categorie per taal horen, anders weet de website niet welke pagina bedoeld is.</summary>
    private static string? Duplicate(AllTexts current, string culture, Guid categoryId, string slug) =>
        current.PerCulture[culture].FirstOrDefault(c => c.CategoryId != categoryId && c.Slug.Value == slug) is { } other
            ? $"De slug \"{slug}\" hoort al bij de categorie \"{other.Name.Value}\"; kies een andere."
            : null;

    /// <summary>Bewaart een aanpassing, of verwijdert ze als de tekst gelijk is aan de standaard.</summary>
    private async Task SetAsync(string culture, WebsiteTextKey key, Guid categoryId, string value, string standard, Guid userId, CancellationToken ct)
    {
        var customization = await db.WebsiteTextCustomizations
            .SingleOrDefaultAsync(t => t.Culture == culture && t.Key == key && t.CategoryId == categoryId, ct);
        if (value == standard)
        {
            if (customization is not null) db.WebsiteTextCustomizations.Remove(customization);
            return;
        }
        if (customization is null)
            db.WebsiteTextCustomizations.Add(customization = new WebsiteTextCustomization { Culture = culture, Key = key, CategoryId = categoryId });
        customization.Value = value;
        customization.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        customization.UpdatedByUserId = userId;
    }

    /// <summary>De vaste slug van een nieuwe categorie, uniek bij de verhuurder: "spellen", anders "spellen-2", ...</summary>
    private static string Unique(string slug, IReadOnlyCollection<string> taken)
    {
        var unique = slug;
        for (var n = 2; taken.Contains(unique); n++) unique = $"{slug}-{n}";
        return unique;
    }

    /// <summary>Bij meer dan één taal zegt een probleem met een slug in welke taal het is.</summary>
    private string InLanguage(string culture, string problem) =>
        Tenant.Cultures.Count > 1 ? $"{LanguageName(culture)}: {problem}" : problem;

    private void CheckCulture(string culture)
    {
        if (!Tenant.Cultures.Contains(culture))
            throw new CategoryException([$"Je biedt de taal '{culture}' niet aan."]);
    }

    private static void Throw(List<string> problems)
    {
        if (problems.Count > 0) throw new CategoryException(problems);
    }

    private static string LanguageName(string culture) => culture switch
    {
        "nl" => "Nederlands",
        "fr" => "Frans",
        "en" => "Engels",
        _ => culture,
    };

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
