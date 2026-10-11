using Moshtar.Application.Website;

namespace Moshtar.Web.Site;

/// <summary>Een categorie zoals de reservatiewebsite ze toont: naam en inleiding in de taal van de pagina, en haar pagina.</summary>
public sealed record CategoryLink(Guid Id, string Name, string Intro, CategoryRoute Route)
{
    /// <summary>Alle categorieën in de volgorde van de verhuurder, in de taal van de pagina.</summary>
    public static async Task<IReadOnlyList<CategoryLink>> AllAsync(IWebsiteTexts texts, SiteLinks links, CancellationToken ct = default)
    {
        var inCulture = (await texts.GetAsync(links.Culture, ct)).Categories;
        var slugs = (await texts.GetCategorySlugsAsync(ct)).ToDictionary(c => c.CategoryId, c => c.PerCulture);
        return inCulture
            .Select(c => new CategoryLink(c.CategoryId, c.Name.Value, c.Intro.Value, new CategoryRoute(c.CategoryId, slugs[c.CategoryId])))
            .ToList();
    }
}
