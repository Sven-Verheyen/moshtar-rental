namespace Moshtar.Domain.Prices;

/// <summary>
/// Periodeprijzen: elke dag van de huurperiode die in de data en op de weekdagen van een periodeprijs valt, wordt zijn
/// percentage duurder (of goedkoper). Gelden er op één dag meerdere voor dezelfde lijn, dan tellen de percentages op.
/// </summary>
public static class PeriodPrices
{
    /// <summary>Valt deze dag van de huurperiode binnen de data (elk optioneel) en op een van de weekdagen (leeg: elke dag)?</summary>
    public static bool AppliesOn(this PricingRule rule, DateOnly day) =>
        rule.Kind == PricingRuleKind.Period
        && (rule.PeriodStart is not { } start || day >= start)
        && (rule.PeriodEnd is not { } end || day <= end)
        && (rule.Weekdays.Count == 0 || rule.Weekdays.Contains(day.DayOfWeek));

    /// <summary>
    /// De som van de periodeprijzen die op deze dag voor deze lijn gelden, in procent; nooit lager dan −100, zodat een
    /// dag nooit minder dan niets kost.
    /// </summary>
    public static decimal DayPercentage(PriceLine line, DateOnly day, IEnumerable<PricingRule> rules) =>
        Math.Max(-100, rules.Where(r => r.AppliesOn(day) && r.Covers(line.Entry, line.CategoryId)).Sum(r => r.Percentage));
}
