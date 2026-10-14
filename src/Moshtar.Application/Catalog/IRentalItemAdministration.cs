using Moshtar.Domain.Availability;

namespace Moshtar.Application.Catalog;

/// <summary>De naam en beschrijving van een artikel in één taal. Een taal zonder naam toont de standaardtaal.</summary>
public sealed record RentalItemText(string Culture, string Name, string? Description = null);

/// <summary>
/// Alles wat de beheerder van een artikel ingeeft. Zonder slug wordt ze uit de naam in de standaardtaal gemaakt. De
/// kenmerken (<see cref="ItemSpecs"/>) zijn allemaal optioneel.
/// </summary>
public sealed record RentalItemInput(
    IReadOnlyList<RentalItemText> Texts,
    Guid? CategoryId,
    int Stock,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    ItemSpecs Specs,
    string? Slug = null);

/// <summary>Een artikel in het overzicht van het back office, in de volgorde van zijn categorie.</summary>
/// <param name="MissingTranslations">De talen waarin de naam of beschrijving nog niet vertaald is.</param>
/// <param name="IsFeatured">Uitgelicht op de home (zie <see cref="IFeaturedAdministration"/>).</param>
public sealed record RentalItemSummary(
    Guid Id,
    string Name,
    string Slug,
    Guid? CategoryId,
    int Stock,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    bool IsActive,
    IReadOnlyList<string> MissingTranslations,
    bool IsFeatured = false);

/// <summary>Een artikel zoals de beheerder het wijzigt.</summary>
/// <param name="Photos">De sleutels van de foto's in de afbeeldingenopslag, de hoofdfoto eerst.</param>
public sealed record RentalItemDetails(Guid Id, bool IsActive, RentalItemInput Input, IReadOnlyList<string> Photos)
{
    public string Slug => Input.Slug ?? "";
}

/// <summary>
/// Een artikel kan zo niet bewaard of verwijderd worden; de boodschap is voor de beheerder bedoeld.
/// <see cref="CanSwitchOff"/> zegt dat het artikel niet verwijderd kan worden, maar wel uitgezet.
/// </summary>
public sealed class RentalItemException(IReadOnlyList<string> problems, bool canSwitchOff = false) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
    public bool CanSwitchOff { get; } = canSwitchOff;
}

/// <summary>
/// De artikelen van de verhuurder beheren, in het back office: aanmaken, wijzigen, volgorde binnen de categorie,
/// uitzetten en verwijderen. De slug is uniek onder de artikelen en pakketten; wordt ze anders, dan stuurt het oude
/// adres door naar het nieuwe (<see cref="IOldSlugs"/>). Elke methode gooit een <see cref="RentalItemException"/> als er
/// iets mis is, en bewaart dan niets.
/// </summary>
public interface IRentalItemAdministration
{
    /// <summary>Alle artikelen, per categorie in de volgorde van de verhuurder.</summary>
    Task<IReadOnlyList<RentalItemSummary>> GetAllAsync(CancellationToken ct = default);

    Task<RentalItemDetails?> GetAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Maakt een artikel aan, achteraan in zijn categorie. Alleen de naam in de standaardtaal is verplicht.</summary>
    Task<Guid> CreateAsync(RentalItemInput input, CancellationToken ct = default);

    /// <summary>
    /// Bewaart een artikel. Een andere categorie zet het achteraan in die categorie. Een voorraad onder wat al
    /// gereserveerd is, mag: het resultaat zijn dan de tekorten van dit artikel vanaf vandaag die het bewaren veroorzaakte
    /// of erger maakte.
    /// </summary>
    Task<IReadOnlyList<Shortage>> SaveAsync(Guid itemId, RentalItemInput input, CancellationToken ct = default);

    /// <summary>Zet de artikelen van een categorie (of zonder categorie) in deze volgorde; de lijst bevat elk ervan precies één keer.</summary>
    Task ReorderAsync(Guid? categoryId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default);

    /// <summary>
    /// Zet een artikel aan of uit. Uitgezet staat het niet meer op de reservatiewebsite en kan het niet meer
    /// gereserveerd worden; bestaande reservaties blijven ongewijzigd.
    /// </summary>
    Task SetActiveAsync(Guid itemId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Uploadt een of meer foto's en zet ze achteraan, tot <see cref="Domain.Catalog.Photo.Max"/> per artikel. Alles of
    /// niets: is er een te veel of is een bestand geen bruikbare afbeelding, dan wordt er geen enkele bewaard.
    /// </summary>
    Task AddPhotosAsync(Guid itemId, IReadOnlyList<PhotoUpload> photos, CancellationToken ct = default);

    /// <summary>Zet de foto's in deze volgorde, de hoofdfoto eerst; de lijst bevat elke sleutel precies één keer.</summary>
    Task ReorderPhotosAsync(Guid itemId, IReadOnlyList<string> keys, CancellationToken ct = default);

    /// <summary>Verwijdert een foto van het artikel, ook uit de afbeeldingenopslag.</summary>
    Task DeletePhotoAsync(Guid itemId, string key, CancellationToken ct = default);

    /// <summary>
    /// Verwijdert een artikel (met zijn foto's), maar alleen als geen reservatie (ook niet via een pakket), pakket of winkelkar ernaar
    /// verwijst. Anders een <see cref="RentalItemException"/> die uitzetten voorstelt.
    /// </summary>
    Task DeleteAsync(Guid itemId, CancellationToken ct = default);
}
