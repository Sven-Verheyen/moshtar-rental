using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Prices;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Prices;

internal sealed partial class PricingRuleAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock) : IPricingRuleAdministration
{
    private const int MaxNameLength = 100;

    public async Task<IReadOnlyList<ComboDiscountSummary>> GetCombosAsync(CancellationToken ct = default)
    {
        var today = Today;
        var rules = await db.PricingRules.AsNoTracking().Where(r => r.Kind == PricingRuleKind.Combo).ToListAsync(ct);
        return rules
            .OrderBy(r => r.IsExpiredOn(today))
            .ThenBy(r => r.NameIn(Tenant.DefaultCulture, Tenant.DefaultCulture), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Id)
            .Select(r => new ComboDiscountSummary(r.Id, new ComboDiscountInput(
                    r.Names.Select(n => new PricingRuleName(n.Culture, n.Name)).ToList(),
                    new PricingTarget(r.AppliesToAll, r.CategoryIds, r.RentalItemIds, r.BundleIds),
                    r.MinimumQuantity ?? 0, r.Percentage, r.IsActive, r.EndDate),
                r.IsExpiredOn(today)))
            .ToList();
    }

    public async Task<Guid> CreateComboAsync(ComboDiscountInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = new PricingRule { Kind = PricingRuleKind.Combo };
        ApplyCombo(rule, input);
        db.PricingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule.Id;
    }

    public async Task SaveComboAsync(Guid ruleId, ComboDiscountInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = await FindAsync(ruleId, ct);
        if (rule.Kind != PricingRuleKind.Combo) throw new PricingRuleException(["Deze prijsregel is geen combinatiekorting."]);
        ApplyCombo(rule, input);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PromotionSummary>> GetPromotionsAsync(CancellationToken ct = default)
    {
        var today = Today;
        var rules = await db.PricingRules.AsNoTracking().Where(r => r.Kind == PricingRuleKind.Promotion).ToListAsync(ct);
        return rules
            .OrderBy(r => r.IsExpiredOn(today))
            .ThenBy(r => r.NameIn(Tenant.DefaultCulture, Tenant.DefaultCulture), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Id)
            .Select(r => new PromotionSummary(r.Id, new PromotionInput(
                    r.Names.Select(n => new PricingRuleName(n.Culture, n.Name)).ToList(),
                    new PricingTarget(r.AppliesToAll, r.CategoryIds, r.RentalItemIds, r.BundleIds),
                    r.Percentage, r.ReservedFrom, r.ReservedUntil, r.RentalFrom, r.RentalUntil, r.IsActive),
                r.IsExpiredOn(today)))
            .ToList();
    }

    public async Task<Guid> CreatePromotionAsync(PromotionInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = new PricingRule { Kind = PricingRuleKind.Promotion };
        ApplyPromotion(rule, input);
        db.PricingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule.Id;
    }

    public async Task SavePromotionAsync(Guid ruleId, PromotionInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var rule = await FindAsync(ruleId, ct);
        if (rule.Kind != PricingRuleKind.Promotion) throw new PricingRuleException(["Deze prijsregel is geen actie."]);
        ApplyPromotion(rule, input);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid ruleId, bool isActive, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        (await FindAsync(ruleId, ct)).IsActive = isActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid ruleId, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        db.PricingRules.Remove(await FindAsync(ruleId, ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Controleert wat de beheerder ingaf en zet het op de regel; gooit een <see cref="PricingRuleException"/> als het niet klopt.</summary>
    private void ApplyCombo(PricingRule rule, ComboDiscountInput input)
    {
        var problems = new List<string>();
        var names = Names(input.Names, problems);
        if (input.Target.IsEmpty || input.Target.All) problems.Add("Kies een categorie of minstens één artikel of pakket.");
        if (input.MinimumQuantity < 2) problems.Add("Het minimum aantal stuks is minstens 2.");
        if (input.Percentage is < 1 or > 100) problems.Add("Het kortingspercentage ligt tussen 1 en 100%.");
        if (problems.Count > 0) throw new PricingRuleException(problems);

        rule.Names = names;
        SetTarget(rule, input.Target);
        rule.MinimumQuantity = input.MinimumQuantity;
        rule.Percentage = Math.Round(input.Percentage, 2);
        rule.IsActive = input.IsActive;
        rule.EndDate = input.EndDate;
    }

    /// <summary>
    /// Controleert een actie en zet ze op de regel. De einddatum (laatste reservatiedag) is ook de einddatum van de
    /// regel: daarna is de actie verlopen en staat ze grijs onderaan.
    /// </summary>
    private void ApplyPromotion(PricingRule rule, PromotionInput input)
    {
        var problems = new List<string>();
        var names = Names(input.Names, problems);
        if (input.Target.IsEmpty) problems.Add("Kies alles, een categorie of minstens één artikel of pakket.");
        if (input.Percentage is < 1 or > 100) problems.Add("Het kortingspercentage ligt tussen 1 en 100%.");
        if (input.ReservedFrom is not { } from || input.ReservedUntil is not { } until) problems.Add("Kies een startdatum en een einddatum.");
        else if (until < from) problems.Add("De einddatum ligt voor de startdatum.");
        if (input.RentalFrom is { } rentalFrom && input.RentalUntil is { } rentalUntil && rentalUntil < rentalFrom)
            problems.Add("Het einde van de huurperiodes ligt voor het begin.");
        if (problems.Count > 0) throw new PricingRuleException(problems);

        rule.Names = names;
        SetTarget(rule, input.Target);
        rule.Percentage = Math.Round(input.Percentage, 2);
        rule.ReservedFrom = input.ReservedFrom;
        rule.ReservedUntil = input.ReservedUntil;
        rule.EndDate = input.ReservedUntil;
        rule.RentalFrom = input.RentalFrom;
        rule.RentalUntil = input.RentalUntil;
        rule.IsActive = input.IsActive;
    }

    /// <summary>De namen per taal die de verhuurder aanbiedt, zonder lege; de naam in de standaardtaal is verplicht.</summary>
    private List<Translation> Names(IReadOnlyList<PricingRuleName> given, List<string> problems)
    {
        foreach (var name in given.Where(n => !Tenant.Cultures.Contains(n.Culture)))
            problems.Add($"Je biedt de taal '{name.Culture}' niet aan.");
        var names = Tenant.Cultures
            .Select(culture => new Translation { Culture = culture, Name = given.FirstOrDefault(n => n.Culture == culture)?.Name.Trim() ?? "" })
            .ToList();
        foreach (var name in names)
        {
            if (name.Culture == Tenant.DefaultCulture && name.Name.Length == 0) problems.Add($"De naam in het {LanguageName(name.Culture)} is leeg.");
            if (name.Name.Length > MaxNameLength) problems.Add($"De naam in het {LanguageName(name.Culture)} is langer dan {MaxNameLength} tekens.");
        }
        return names.Where(n => n.Name.Length > 0).ToList();
    }

    private static void SetTarget(PricingRule rule, PricingTarget target)
    {
        rule.AppliesToAll = target.All;
        rule.CategoryIds = target.All ? [] : target.CategoryIds.Distinct().ToList();
        rule.RentalItemIds = target.All ? [] : target.RentalItemIds.Distinct().ToList();
        rule.BundleIds = target.All ? [] : target.BundleIds.Distinct().ToList();
    }

    private async Task<PricingRule> FindAsync(Guid ruleId, CancellationToken ct) =>
        await db.PricingRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct)
            ?? throw new PricingRuleException(["Deze prijsregel bestaat niet (meer)."]);

    private DateOnly Today => Belgium.Today(clock);

    private static string LanguageName(string culture) => culture switch
    {
        "nl" => "Nederlands",
        "fr" => "Frans",
        "en" => "Engels",
        _ => culture,
    };

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
