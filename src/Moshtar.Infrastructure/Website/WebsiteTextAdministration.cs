using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Website;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Website;

internal sealed class WebsiteTextAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock) : IWebsiteTextAdministration
{
    private const int MaxTitleLength = 150;
    private const int MaxSubtitleLength = 300;
    private const int MaxCategoryNameLength = 100;
    private const int MaxIntroLength = 2000;
    private const int MaxQuestionLength = 300;
    private const int MaxAnswerLength = 2000;
    private const int MaxFaqEntries = 30;

    public async Task<WebsiteTexts> GetAsync(string culture, CancellationToken ct = default)
    {
        var customizations = await db.WebsiteTextCustomizations.AsNoTracking().Where(t => t.Culture == culture).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct);
        return Texts(culture, customizations, categories);
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

    public async Task SaveCategoryAsync(Guid categoryId, string culture, string name, string slug, string intro, Guid userId, CancellationToken ct = default)
    {
        CheckCulture(culture);
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new WebsiteTextException(["Deze categorie bestaat niet."]);
        (name, slug, intro) = (name.Trim(), slug.Trim(), intro.Trim().ReplaceLineEndings("\n"));
        var problems = new List<string>();
        Required(problems, name, "De naam", MaxCategoryNameLength);
        if (CategorySlug.Problem(slug) is { } slugProblem) problems.Add(slugProblem);
        if (intro.Length > MaxIntroLength) problems.Add($"De inleiding is langer dan {MaxIntroLength} tekens.");
        Throw(problems);
        await CheckUniqueSlugAsync(culture, categoryId, slug, ct);

        var standardName = StandardName(category, culture);
        await SetAsync(culture, WebsiteTextKey.CategoryName, categoryId, name, standardName, userId, ct);
        await SetAsync(culture, WebsiteTextKey.CategorySlug, categoryId, slug, StandardSlug(category, culture), userId, ct);
        await SetAsync(culture, WebsiteTextKey.CategoryIntro, categoryId, intro, "", userId, ct);
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

    public async Task ResetAsync(string culture, WebsiteTextKey key, Guid? categoryId = null, CancellationToken ct = default)
    {
        if (key == WebsiteTextKey.CategorySlug && categoryId is { } id
            && await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) is { } category)
            await CheckUniqueSlugAsync(culture, id, StandardSlug(category, culture), ct);
        await db.WebsiteTextCustomizations
            .Where(t => t.Culture == culture && t.Key == key && t.CategoryId == categoryId)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>De teksten zonder aanpassingen, om te zien of een bewaarde tekst gelijk is aan de standaard.</summary>
    private async Task<WebsiteTexts> GetStandardAsync(string culture, CancellationToken ct) =>
        Texts(culture, [], await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct));

    private WebsiteTexts Texts(string culture, IReadOnlyList<WebsiteTextCustomization> customizations, IReadOnlyList<Category> categories)
    {
        WebsiteText Text(WebsiteTextKey key, Guid? categoryId, string standard) =>
            customizations.FirstOrDefault(c => c.Key == key && c.CategoryId == categoryId) is { } c
                ? new WebsiteText(c.Value, true)
                : new WebsiteText(standard, false);

        var faq = customizations.FirstOrDefault(c => c.Key == WebsiteTextKey.Faq && c.CategoryId == null);
        return new WebsiteTexts(
            culture,
            Text(WebsiteTextKey.HomeTitle, null, WebsiteTextStandard.HomeTitle(culture, Tenant.Name)),
            Text(WebsiteTextKey.HomeSubtitle, null, WebsiteTextStandard.HomeSubtitle(culture, Tenant.ServiceArea)),
            faq is null ? WebsiteTextStandard.Faq(culture, Tenant.ServiceArea) : JsonSerializer.Deserialize<List<FaqEntry>>(faq.Value) ?? [],
            faq is not null,
            categories.Select(c => new CategoryTexts(
                c.Id,
                Text(WebsiteTextKey.CategoryName, c.Id, StandardName(c, culture)),
                Text(WebsiteTextKey.CategorySlug, c.Id, StandardSlug(c, culture)),
                Text(WebsiteTextKey.CategoryIntro, c.Id, ""))).ToList());
    }

    /// <summary>Een slug mag maar bij één categorie per taal horen, anders weet de website niet welke pagina bedoeld is.</summary>
    private async Task CheckUniqueSlugAsync(string culture, Guid categoryId, string slug, CancellationToken ct)
    {
        var texts = await GetAsync(culture, ct);
        if (texts.Categories.FirstOrDefault(c => c.CategoryId != categoryId && c.Slug.Value == slug) is { } other)
            throw new WebsiteTextException([$"De slug \"{slug}\" hoort al bij de categorie \"{other.Name.Value}\"; kies een andere."]);
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

    private string StandardName(Category category, string culture) =>
        category.Translations.For(culture, Tenant.DefaultCulture)?.Name ?? category.Slug;

    private string StandardSlug(Category category, string culture) =>
        CategorySlug.From(StandardName(category, culture)) is { Length: > 0 } slug && CategorySlug.Problem(slug) is null ? slug : category.Slug;

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
