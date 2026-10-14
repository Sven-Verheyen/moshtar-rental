using Moshtar.Application.Website;
using Moshtar.Domain.Website;

namespace Moshtar.Application.Catalog;

/// <summary>De naam en eventueel een eigen slug van een nieuwe categorie in één taal. Zonder slug wordt ze uit de naam gemaakt.</summary>
public sealed record NewCategoryText(string Culture, string Name, string? Slug = null);

/// <summary>Een categorie zoals het back office ze toont: haar teksten in elke taal van de verhuurder en hoeveel artikelen erin zitten.</summary>
public sealed record CategoryDetails(Guid Id, int ItemCount, IReadOnlyDictionary<string, CategoryTexts> Texts);

/// <summary>Een categorie kan zo niet bewaard of verwijderd worden; de boodschap is voor de beheerder bedoeld.</summary>
public sealed class CategoryException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// De categorieën van de verhuurder beheren, in het back office: aanmaken, naam, slug en inleiding per taal, volgorde
/// en verwijderen. Een slug moet uniek zijn onder de categorieën in een taal; wordt de slug van een categorie anders,
/// dan stuurt het oude adres door naar het nieuwe (<see cref="IOldSlugs"/>). Elke methode gooit een
/// <see cref="CategoryException"/> als er iets mis is, en bewaart dan niets.
/// </summary>
public interface ICategoryAdministration
{
    /// <summary>Alle categorieën in de volgorde van de verhuurder.</summary>
    Task<IReadOnlyList<CategoryDetails>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Maakt een categorie aan, achteraan. De naam in de standaardtaal is verplicht; een taal zonder naam toont de naam in
    /// de standaardtaal.
    /// </summary>
    Task<Guid> CreateAsync(IReadOnlyList<NewCategoryText> texts, Guid userId, CancellationToken ct = default);

    /// <summary>Bewaart naam, slug en inleiding van een categorie in één taal.</summary>
    Task SaveAsync(Guid categoryId, string culture, string name, string slug, string intro, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Zet één tekst van een categorie (<see cref="WebsiteTextKey.CategoryName"/>, <see cref="WebsiteTextKey.CategorySlug"/>
    /// of <see cref="WebsiteTextKey.CategoryIntro"/>) in één taal terug naar de standaard.
    /// </summary>
    Task ResetAsync(Guid categoryId, string culture, WebsiteTextKey key, CancellationToken ct = default);

    /// <summary>Zet de categorieën in deze volgorde; de lijst bevat elke categorie precies één keer.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> categoryIds, CancellationToken ct = default);

    /// <summary>Verwijdert een categorie, maar alleen als er geen artikelen in zitten.</summary>
    Task DeleteAsync(Guid categoryId, CancellationToken ct = default);
}
