using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;
using Moshtar.Domain.Website;

namespace Moshtar.Infrastructure.Website;

/// <summary>
/// De naam, slug en inleiding van elke categorie in één taal, zoals ze gelden: de aanpassing van de verhuurder, of de
/// standaard uit de vertaalde naam van de categorie. Gedeeld door de websiteteksten en het categoriebeheer.
/// </summary>
internal static class CategoryTextResolver
{
    /// <param name="customizations">De aanpassingen in deze taal; andere teksten worden genegeerd.</param>
    /// <param name="categories">De categorieën in de volgorde van de verhuurder.</param>
    public static IReadOnlyList<CategoryTexts> Resolve(
        Tenant tenant, string culture, IEnumerable<WebsiteTextCustomization> customizations, IReadOnlyList<Category> categories)
    {
        var mine = customizations.Where(c => c.Culture == culture && c.CategoryId is not null).ToList();

        WebsiteText Text(WebsiteTextKey key, Guid categoryId, string standard) =>
            mine.FirstOrDefault(c => c.Key == key && c.CategoryId == categoryId) is { } c
                ? new WebsiteText(c.Value, true)
                : new WebsiteText(standard, false);

        // Twee namen kunnen dezelfde standaardslug geven ("Spellen" en "Spellen!"); dan krijgt de latere categorie
        // haar vaste slug, en anders een volgnummer, zodat elke slug in deze taal maar bij één categorie hoort.
        var used = mine.Where(c => c.Key == WebsiteTextKey.CategorySlug).Select(c => c.Value).ToHashSet();
        var standardSlugs = new Dictionary<Guid, string>();
        foreach (var category in categories.Where(c => !mine.Any(x => x.Key == WebsiteTextKey.CategorySlug && x.CategoryId == c.Id)))
        {
            var slug = StandardSlug(tenant, category, culture);
            if (used.Contains(slug) && CategorySlug.Problem(category.Slug) is null) slug = category.Slug;
            for (var n = 2; used.Contains(slug); n++) slug = $"{StandardSlug(tenant, category, culture)}-{n}";
            used.Add(slug);
            standardSlugs[category.Id] = slug;
        }

        return categories.Select(c => new CategoryTexts(
            c.Id,
            Text(WebsiteTextKey.CategoryName, c.Id, StandardName(tenant, c, culture)),
            Text(WebsiteTextKey.CategorySlug, c.Id, standardSlugs.GetValueOrDefault(c.Id) ?? StandardSlug(tenant, c, culture)),
            Text(WebsiteTextKey.CategoryIntro, c.Id, ""))).ToList();
    }

    private static string StandardName(Tenant tenant, Category category, string culture) =>
        category.Translations.For(culture, tenant.DefaultCulture)?.Name ?? category.Slug;

    private static string StandardSlug(Tenant tenant, Category category, string culture) =>
        CategorySlug.From(StandardName(tenant, category, culture)) is { Length: > 0 } slug && CategorySlug.Problem(slug) is null ? slug : category.Slug;
}
