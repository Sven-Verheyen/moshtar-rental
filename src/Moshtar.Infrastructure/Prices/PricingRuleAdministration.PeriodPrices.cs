using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Prices;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;

namespace Moshtar.Infrastructure.Prices;

/// <summary>Periodeprijzen beheren: zoals combinatiekortingen, met één naam die alleen in het back office staat.</summary>
internal sealed partial class PricingRuleAdministration
{
    private const decimal MinPeriodPercentage = -90;
    private const decimal MaxPeriodPercentage = 200;

    public async Task<IReadOnlyList<PeriodPriceSummary>> GetPeriodPricesAsync(CancellationToken ct = default)
    {
        var today = Today;
        return (await ListAsync(PricingRuleKind.Period, today, ct))
            .Select(r => new PeriodPriceSummary(r.Id, new PeriodPriceInput(
                    r.NameIn(Tenant.DefaultCulture, Tenant.DefaultCulture),
                    new PricingTarget(r.AppliesToAll, r.CategoryIds, r.RentalItemIds, r.BundleIds),
                    r.Percentage, r.PeriodStart, r.PeriodEnd, r.Weekdays.Order().ToList(), r.IsActive, r.EndDate),
                r.IsExpiredOn(today)))
            .ToList();
    }

    public async Task<Guid> CreatePeriodPriceAsync(PeriodPriceInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = new PricingRule { Kind = PricingRuleKind.Period };
        ApplyPeriodPrice(rule, input);
        db.PricingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule.Id;
    }

    public async Task SavePeriodPriceAsync(Guid ruleId, PeriodPriceInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = await FindAsync(ruleId, ct);
        if (rule.Kind != PricingRuleKind.Period) throw new PricingRuleException(["Deze prijsregel is geen periodeprijs."]);
        ApplyPeriodPrice(rule, input);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Controleert wat de beheerder ingaf en zet het op de regel; gooit een <see cref="PricingRuleException"/> als het niet klopt.</summary>
    private void ApplyPeriodPrice(PricingRule rule, PeriodPriceInput input)
    {
        var problems = new List<string>();
        var name = input.Name.Trim();
        if (name.Length == 0) problems.Add("De naam is leeg.");
        if (name.Length > MaxNameLength) problems.Add($"De naam is langer dan {MaxNameLength} tekens.");
        if (input.From is null && input.Until is null && input.Weekdays.Count == 0) problems.Add("Kies data, weekdagen of allebei.");
        if (input.From is { } from && input.Until is { } until && from > until) problems.Add("De begindatum ligt na de einddatum.");
        if (input.Percentage is < MinPeriodPercentage or > MaxPeriodPercentage) problems.Add("Het percentage ligt tussen −90% en +200%.");
        else if (input.Percentage == 0) problems.Add("Een periodeprijs van 0% verandert niets.");
        if (problems.Count > 0) throw new PricingRuleException(problems);

        rule.Names = [new Translation { Culture = Tenant.DefaultCulture, Name = name }];
        // Zonder doel werkt een periodeprijs op alles.
        SetTarget(rule, input.Target.IsEmpty ? PricingTarget.Everything : input.Target);
        rule.Percentage = Math.Round(input.Percentage, 2);
        rule.PeriodStart = input.From;
        rule.PeriodEnd = input.Until;
        rule.Weekdays = input.Weekdays.Distinct().Order().ToList();
        rule.IsActive = input.IsActive;
        rule.EndDate = input.EndDate;
    }
}
