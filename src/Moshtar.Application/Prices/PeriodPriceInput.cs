namespace Moshtar.Application.Prices;

/// <summary>
/// Wat de beheerder van een periodeprijs ingeeft: elke dag van de huurperiode tussen <see cref="From"/> en
/// <see cref="Until"/> (elk optioneel, inbegrepen) en op een van de <see cref="Weekdays"/> (leeg: elke dag) wordt
/// <see cref="Percentage"/> duurder (positief) of goedkoper (negatief). De naam is alleen voor het back office. Zonder
/// doel werkt de periodeprijs op alles, ook op pakketten.
/// </summary>
public sealed record PeriodPriceInput(
    string Name,
    PricingTarget Target,
    decimal Percentage,
    DateOnly? From,
    DateOnly? Until,
    IReadOnlyList<DayOfWeek> Weekdays,
    bool IsActive = true,
    DateOnly? EndDate = null);

/// <summary>Een periodeprijs in het back office. <see cref="IsExpired"/>: de einddatum is voorbij, de regel staat grijs onderaan.</summary>
public sealed record PeriodPriceSummary(Guid Id, PeriodPriceInput Input, bool IsExpired);
