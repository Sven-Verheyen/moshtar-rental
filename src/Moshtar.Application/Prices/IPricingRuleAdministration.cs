namespace Moshtar.Application.Prices;

/// <summary>De naam van een prijsregel in één taal. Een taal zonder naam toont de naam in de standaardtaal.</summary>
public sealed record PricingRuleName(string Culture, string Name);

/// <summary>
/// Waarop een prijsregel werkt: alles, of de gekozen categorieën, artikelen en pakketten. Een categorie omvat enkel
/// losse artikelen; een pakket telt alleen als het zelf gekozen is.
/// </summary>
public sealed record PricingTarget(bool All, IReadOnlyList<Guid> CategoryIds, IReadOnlyList<Guid> RentalItemIds, IReadOnlyList<Guid> BundleIds)
{
    public static PricingTarget Everything { get; } = new(true, [], [], []);

    public static PricingTarget Categories(params Guid[] categoryIds) => new(false, categoryIds, [], []);

    public static PricingTarget Chosen(IReadOnlyList<Guid> rentalItemIds, IReadOnlyList<Guid> bundleIds) => new(false, [], rentalItemIds, bundleIds);

    public bool IsEmpty => !All && CategoryIds.Count == 0 && RentalItemIds.Count == 0 && BundleIds.Count == 0;
}

/// <summary>
/// Wat de beheerder van een combinatiekorting ingeeft: bij minstens <see cref="MinimumQuantity"/> stuks uit het doel in
/// één reservatie krijgt het goedkoopste stuk van elke volledige groep <see cref="Percentage"/> korting. Zonder
/// einddatum loopt de regel tot hij uitgezet of verwijderd wordt.
/// </summary>
public sealed record ComboDiscountInput(
    IReadOnlyList<PricingRuleName> Names,
    PricingTarget Target,
    int MinimumQuantity,
    decimal Percentage,
    bool IsActive = true,
    DateOnly? EndDate = null);

/// <summary>Een combinatiekorting in het back office. <see cref="IsExpired"/>: de einddatum is voorbij, de regel staat grijs onderaan.</summary>
public sealed record ComboDiscountSummary(Guid Id, ComboDiscountInput Input, bool IsExpired);

/// <summary>Een prijsregel kan zo niet bewaard worden; de boodschappen zijn voor de beheerder bedoeld.</summary>
public sealed class PricingRuleException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// De prijsregels van de verhuurder beheren, in het back office (alleen de Beheerder): aanmaken, wijzigen, aan/uit en
/// verwijderen, wanneer hij wil. Bestaande reservaties houden hun prijzen en kortingslijnen. Periodeprijzen en acties
/// krijgen hier hun eigen methodes naast de combinatiekortingen.
/// </summary>
public interface IPricingRuleAdministration
{
    /// <summary>De combinatiekortingen: eerst die nog lopen (ook uitgezette), op naam; daaronder de verlopen.</summary>
    Task<IReadOnlyList<ComboDiscountSummary>> GetCombosAsync(CancellationToken ct = default);

    /// <summary>Gooit een <see cref="PricingRuleException"/> als de invoer niet klopt, en bewaart dan niets.</summary>
    Task<Guid> CreateComboAsync(ComboDiscountInput input, CancellationToken ct = default);

    /// <summary>Gooit een <see cref="PricingRuleException"/> als de invoer niet klopt, en bewaart dan niets.</summary>
    Task SaveComboAsync(Guid ruleId, ComboDiscountInput input, CancellationToken ct = default);

    /// <summary>Zet een prijsregel van elke soort aan of uit.</summary>
    Task SetActiveAsync(Guid ruleId, bool isActive, CancellationToken ct = default);

    /// <summary>Verwijdert een prijsregel van elke soort.</summary>
    Task DeleteAsync(Guid ruleId, CancellationToken ct = default);
}
