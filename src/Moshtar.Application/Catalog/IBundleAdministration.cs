namespace Moshtar.Application.Catalog;

/// <summary>Een artikel in een pakket, met hoeveel exemplaren ervan.</summary>
public sealed record BundleContent(Guid RentalItemId, int Quantity);

/// <summary>
/// Alles wat de beheerder van een pakket ingeeft: naam en beschrijving per taal (zoals bij een artikel, alleen de naam in
/// de standaardtaal is verplicht), de artikelen met hun aantallen en de eigen prijs. Zonder slug wordt ze uit de naam in
/// de standaardtaal gemaakt.
/// </summary>
public sealed record BundleInput(
    IReadOnlyList<RentalItemText> Texts,
    IReadOnlyList<BundleContent> Contents,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    string? Slug = null);

/// <summary>Een pakket in het overzicht van het back office.</summary>
/// <param name="Contents">Wat erin zit, bv. "2 × Cornhole", in de standaardtaal.</param>
/// <param name="SwitchedOffItems">
/// De namen van de uitgezette artikelen erin. Zolang er een is, staat het pakket niet op de reservatiewebsite, ook al
/// staat het zelf aan.
/// </param>
/// <param name="MissingTranslations">De talen waarin de naam of beschrijving nog niet vertaald is.</param>
/// <param name="IsFeatured">Uitgelicht op de home (zie <see cref="IFeaturedAdministration"/>).</param>
public sealed record BundleSummary(
    Guid Id,
    string Name,
    string Slug,
    decimal DayPrice,
    decimal? ExtraDayPrice,
    bool IsActive,
    IReadOnlyList<string> Contents,
    IReadOnlyList<string> SwitchedOffItems,
    IReadOnlyList<string> MissingTranslations,
    bool IsFeatured = false)
{
    /// <summary>Staat het pakket op de reservatiewebsite: het staat zelf aan en al zijn artikelen ook.</summary>
    public bool IsVisible => IsActive && SwitchedOffItems.Count == 0;
}

/// <summary>Een pakket zoals de beheerder het wijzigt.</summary>
/// <param name="Photos">De sleutels van de eigen foto's in de afbeeldingenopslag, de hoofdfoto eerst; leeg als het pakket die van zijn artikelen toont.</param>
/// <param name="SwitchedOffItems">De namen van de uitgezette artikelen erin (zie <see cref="BundleSummary.SwitchedOffItems"/>).</param>
public sealed record BundleDetails(Guid Id, bool IsActive, BundleInput Input, IReadOnlyList<string> Photos, IReadOnlyList<string> SwitchedOffItems)
{
    public string Slug => Input.Slug ?? "";
}

/// <summary>
/// Een pakket kan zo niet bewaard of verwijderd worden; de boodschap is voor de beheerder bedoeld.
/// <see cref="CanSwitchOff"/> zegt dat het pakket niet verwijderd kan worden, maar wel uitgezet.
/// </summary>
public sealed class BundleException(IReadOnlyList<string> problems, bool canSwitchOff = false) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
    public bool CanSwitchOff { get; } = canSwitchOff;
}

/// <summary>
/// De pakketten van de verhuurder beheren, in het back office: aanmaken, wijzigen, foto's, uitzetten en verwijderen. De
/// slug is uniek onder de artikelen en pakketten; wordt ze anders, dan stuurt het oude adres door naar het nieuwe
/// (<see cref="IOldSlugs"/>). De beschikbaarheid van een pakket volgt uit die van zijn artikelen. Elke methode gooit een
/// <see cref="BundleException"/> als er iets mis is, en bewaart dan niets.
/// </summary>
public interface IBundleAdministration
{
    /// <summary>Alle pakketten, op naam.</summary>
    Task<IReadOnlyList<BundleSummary>> GetAllAsync(CancellationToken ct = default);

    Task<BundleDetails?> GetAsync(Guid bundleId, CancellationToken ct = default);

    /// <summary>Maakt een pakket aan met minstens één artikel. Alleen de naam in de standaardtaal is verplicht.</summary>
    Task<Guid> CreateAsync(BundleInput input, CancellationToken ct = default);

    /// <summary>Bewaart een pakket. Bestaande reservaties van het pakket blijven ongewijzigd.</summary>
    Task SaveAsync(Guid bundleId, BundleInput input, CancellationToken ct = default);

    /// <summary>
    /// Zet een pakket aan of uit. Uitgezet staat het niet meer op de reservatiewebsite en kan het niet meer gereserveerd
    /// worden; bestaande reservaties blijven ongewijzigd.
    /// </summary>
    Task SetActiveAsync(Guid bundleId, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Uploadt een of meer eigen foto's en zet ze achteraan, tot <see cref="Domain.Catalog.Photo.Max"/> per pakket. Alles
    /// of niets, zoals bij een artikel.
    /// </summary>
    Task AddPhotosAsync(Guid bundleId, IReadOnlyList<PhotoUpload> photos, CancellationToken ct = default);

    /// <summary>Zet de foto's in deze volgorde, de hoofdfoto eerst; de lijst bevat elke sleutel precies één keer.</summary>
    Task ReorderPhotosAsync(Guid bundleId, IReadOnlyList<string> keys, CancellationToken ct = default);

    /// <summary>Verwijdert een eigen foto van het pakket, ook uit de afbeeldingenopslag.</summary>
    Task DeletePhotoAsync(Guid bundleId, string key, CancellationToken ct = default);

    /// <summary>
    /// Verwijdert een pakket (met zijn foto's), maar alleen als geen reservatie en geen winkelkar ernaar verwijst. Anders
    /// een <see cref="BundleException"/> die uitzetten voorstelt.
    /// </summary>
    Task DeleteAsync(Guid bundleId, CancellationToken ct = default);
}
