using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Website;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Website;

internal sealed class WebsiteTextAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock) : IWebsiteTextAdministration
{
    private const int MaxTitleLength = 150;
    private const int MaxSubtitleLength = 300;
    private const int MaxQuestionLength = 300;
    private const int MaxAnswerLength = 2000;
    private const int MaxFaqEntries = 30;

    public async Task<WebsiteTexts> GetAsync(string culture, CancellationToken ct = default)
    {
        var customizations = await db.WebsiteTextCustomizations.AsNoTracking().Where(t => t.Culture == culture).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        return Texts(culture, customizations, categories);
    }

    public async Task<IReadOnlyList<CategorySlugs>> GetCategorySlugsAsync(CancellationToken ct = default)
    {
        var customizations = await db.WebsiteTextCustomizations.AsNoTracking().Where(t => t.Key == WebsiteTextKey.CategorySlug).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        var perCulture = Tenant.Cultures.ToDictionary(
            culture => culture,
            culture => CategoryTextResolver.Resolve(Tenant, culture, customizations, categories));
        return categories.Select(c => new CategorySlugs(
            c.Id,
            perCulture.ToDictionary(kv => kv.Key, kv => kv.Value.Single(t => t.CategoryId == c.Id).Slug.Value))).ToList();
    }

    public async Task SaveHomeAsync(string culture, string title, string subtitle, Guid userId, CancellationToken ct = default)
    {
        CheckCulture(culture);
        (title, subtitle) = (title.Trim(), subtitle.Trim());
        var problems = new List<string>();
        Required(problems, title, "De titel", MaxTitleLength);
        Required(problems, subtitle, "De ondertitel", MaxSubtitleLength);
        Throw(problems);

        var standard = await GetStandardAsync(culture, ct);
        await SetAsync(culture, WebsiteTextKey.HomeTitle, null, title, standard.HomeTitle.Value, userId, ct);
        await SetAsync(culture, WebsiteTextKey.HomeSubtitle, null, subtitle, standard.HomeSubtitle.Value, userId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveFaqAsync(string culture, IReadOnlyList<FaqEntry> faq, Guid userId, CancellationToken ct = default)
    {
        CheckCulture(culture);
        faq = faq.Select(e => new FaqEntry(e.Question.Trim(), e.Answer.Trim().ReplaceLineEndings("\n"))).ToList();
        var problems = new List<string>();
        if (faq.Count > MaxFaqEntries) problems.Add($"Je kan hoogstens {MaxFaqEntries} veelgestelde vragen hebben.");
        foreach (var (entry, number) in faq.Select((e, i) => (e, i + 1)))
        {
            Required(problems, entry.Question, $"Vraag {number}", MaxQuestionLength);
            Required(problems, entry.Answer, $"Het antwoord op vraag {number}", MaxAnswerLength);
        }
        Throw(problems);

        var standard = await GetStandardAsync(culture, ct);
        await SetAsync(culture, WebsiteTextKey.Faq, null, Json(faq), Json(standard.Faq), userId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(string culture, WebsiteTextKey key, CancellationToken ct = default)
    {
        if (key is WebsiteTextKey.CategoryName or WebsiteTextKey.CategorySlug or WebsiteTextKey.CategoryIntro)
            throw new ArgumentException("Categorieteksten worden bij de categorieën beheerd.", nameof(key));
        await db.WebsiteTextCustomizations
            .Where(t => t.Culture == culture && t.Key == key && t.CategoryId == null)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>De teksten zonder aanpassingen, om te zien of een bewaarde tekst gelijk is aan de standaard.</summary>
    private async Task<WebsiteTexts> GetStandardAsync(string culture, CancellationToken ct) =>
        Texts(culture, [], await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct));

    private WebsiteTexts Texts(string culture, IReadOnlyList<WebsiteTextCustomization> customizations, IReadOnlyList<Category> categories)
    {
        WebsiteText Text(WebsiteTextKey key, string standard) =>
            customizations.FirstOrDefault(c => c.Key == key && c.CategoryId == null) is { } c
                ? new WebsiteText(c.Value, true)
                : new WebsiteText(standard, false);

        var faq = customizations.FirstOrDefault(c => c.Key == WebsiteTextKey.Faq && c.CategoryId == null);
        return new WebsiteTexts(
            culture,
            Text(WebsiteTextKey.HomeTitle, WebsiteTextStandard.HomeTitle(culture, Tenant.Name)),
            Text(WebsiteTextKey.HomeSubtitle, WebsiteTextStandard.HomeSubtitle(culture, Tenant.ServiceArea)),
            faq is null ? WebsiteTextStandard.Faq(culture, Tenant.ServiceArea) : JsonSerializer.Deserialize<List<FaqEntry>>(faq.Value) ?? [],
            faq is not null,
            CategoryTextResolver.Resolve(Tenant, culture, customizations, categories));
    }

    /// <summary>Bewaart een aanpassing, of verwijdert ze als de tekst gelijk is aan de standaard.</summary>
    private async Task SetAsync(string culture, WebsiteTextKey key, Guid? categoryId, string value, string standard, Guid userId, CancellationToken ct)
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

    private static string Json(IReadOnlyList<FaqEntry> faq) => JsonSerializer.Serialize(faq);

    private static void Required(List<string> problems, string value, string what, int maxLength)
    {
        if (value.Length == 0) problems.Add($"{what} is leeg.");
        else if (value.Length > maxLength) problems.Add($"{what} is langer dan {maxLength} tekens.");
    }

    private static void Throw(List<string> problems)
    {
        if (problems.Count > 0) throw new WebsiteTextException(problems);
    }

    private void CheckCulture(string culture)
    {
        if (!Tenant.Cultures.Contains(culture))
            throw new WebsiteTextException([$"Je biedt de taal '{culture}' niet aan."]);
    }

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
