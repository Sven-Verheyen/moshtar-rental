using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Prices;

/// <summary>De drie soorten prijsregels.</summary>
public enum PricingRuleKind
{
    /// <summary>Combinatiekorting: het goedkoopste stuk van elke volledige groep krijgt een korting.</summary>
    Combo = 1,
    /// <summary>Periodeprijs: elke dag in bepaalde data of op bepaalde weekdagen wordt een percentage duurder of goedkoper.</summary>
    Period = 2,
    /// <summary>Actie: een tijdelijke korting voor reservaties gemaakt tussen een start- en einddatum.</summary>
    Promotion = 3,
}

/// <summary>
/// Een prijsregel van een verhuurder: een combinatiekorting, een periodeprijs of een actie. Welke velden gelden, hangt
/// af van <see cref="Kind"/>. Een reservatie bewaart haar prijzen en kortingslijnen: een regel die later wijzigt of
/// verdwijnt, verandert ze niet.
/// </summary>
public class PricingRule : TenantEntity
{
    public PricingRuleKind Kind { get; set; }

    /// <summary>Aan of uit; een uitgezette regel telt nergens mee.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>De laatste reservatiedatum waarop de regel geldt, of null: geen einddatum. Daarna is de regel verlopen.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Het percentage: voor een combinatiekorting en een actie de korting (1 tot 100), voor een periodeprijs hoeveel
    /// duurder (positief) of goedkoper (negatief) de dag wordt (−90 tot +200).
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>De naam per taal, voor een combinatiekorting en een actie: op de kortingslijn en de artikelpagina.</summary>
    public List<Translation> Names { get; set; } = [];

    // Waarop de regel werkt. Een periodeprijs zonder doel werkt op alles (AppliesToAll); andere regels op de gekozen
    // categorieën, artikelen en pakketten. Een categorie omvat enkel losse artikelen: een pakket telt alleen als het zelf
    // gekozen is (of bij AppliesToAll).
    public bool AppliesToAll { get; set; }
    public List<Guid> CategoryIds { get; set; } = [];
    public List<Guid> RentalItemIds { get; set; } = [];
    public List<Guid> BundleIds { get; set; } = [];

    /// <summary>Combinatiekorting: vanaf zoveel stuks in het bereik van de regel, en per volledige groep van zoveel.</summary>
    public int? MinimumQuantity { get; set; }

    /// <summary>Periodeprijs: de dagen van de huurperiode tussen deze data (elk optioneel, inbegrepen).</summary>
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Periodeprijs: de weekdagen waarop hij geldt; leeg is elke dag.</summary>
    public List<DayOfWeek> Weekdays { get; set; } = [];

    /// <summary>Actie: voor reservaties gemaakt tussen deze data (inbegrepen).</summary>
    public DateOnly? ReservedFrom { get; set; }
    public DateOnly? ReservedUntil { get; set; }

    /// <summary>Actie: optioneel enkel voor huurperiodes binnen deze data (inbegrepen).</summary>
    public DateOnly? RentalFrom { get; set; }
    public DateOnly? RentalUntil { get; set; }

    /// <summary>Geldt de regel voor een reservatie gemaakt op deze dag? Aan, en niet verlopen.</summary>
    public bool IsInForceOn(DateOnly reservationDate) => IsActive && !IsExpiredOn(reservationDate);

    /// <summary>Is de einddatum voorbij? Verlopen regels staan grijs onderaan in het back office.</summary>
    public bool IsExpiredOn(DateOnly today) => EndDate is { } end && end < today;

    /// <summary>
    /// Valt dit artikel of pakket in het bereik van de regel? Een artikel via zijn categorie of zelf, een pakket alleen
    /// als het zelf gekozen is; de artikelen in een pakket tellen nooit apart.
    /// </summary>
    public bool Covers(CatalogRef entry, Guid? categoryId) =>
        AppliesToAll
        || entry.BundleId is { } bundleId && BundleIds.Contains(bundleId)
        || entry.RentalItemId is { } itemId && (RentalItemIds.Contains(itemId) || categoryId is { } c && CategoryIds.Contains(c));

    /// <summary>De naam in deze taal, anders in de standaardtaal, anders de eerste.</summary>
    public string NameIn(string culture, string fallbackCulture) => Names.For(culture, fallbackCulture)?.Name ?? "";
}
