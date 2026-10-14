namespace Moshtar.Application.Catalog;

/// <summary>Een uitgelicht artikel of pakket in de lijst van het back office.</summary>
/// <param name="IsVisible">
/// Staat het op de reservatiewebsite: een artikel als het aan staat, een pakket als het zelf aan staat en al zijn
/// artikelen ook. Een uitgelicht item dat niet zichtbaar is, verschijnt niet op de home.
/// </param>
public sealed record FeaturedEntry(Guid Id, CatalogEntryKind Kind, string Name, bool IsVisible);

/// <summary>Een artikel of pakket kan zo niet uitgelicht worden; de boodschap is voor de beheerder bedoeld.</summary>
public sealed class FeaturedException(string problem) : Exception(problem);

/// <summary>
/// Artikelen en pakketten uitlichten, in het back office. De uitgelichte artikelen en pakketten staan samen in één
/// volgorde, die de home volgt in plaats van "Populair". Is niets uitgelicht, dan toont de home de pakketten en het
/// eerste artikel van elke categorie.
/// </summary>
public interface IFeaturedAdministration
{
    /// <summary>Alle uitgelichte artikelen en pakketten samen, in hun volgorde.</summary>
    Task<IReadOnlyList<FeaturedEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Licht een artikel of pakket uit (achteraan in de lijst) of niet meer. Een artikel of pakket dat al uitgelicht is,
    /// behoudt zijn plaats.
    /// </summary>
    Task SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken ct = default);

    /// <summary>Zet de uitgelichte artikelen en pakketten in deze volgorde; de lijst bevat elk ervan precies één keer.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
