using Moshtar.Domain.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;

namespace Moshtar.Domain.Tests;

/// <summary>
/// De ene prijsberekening van een reservatie of winkelkar: per lijn de prijs per stuk, en de kortingen van de
/// prijsregels als eigen lijnen. Een combinatiekorting geeft het goedkoopste stuk van elke volledige groep de korting.
/// </summary>
public class PriceCalculatorTests
{
    private static readonly Guid Castles = Guid.NewGuid();
    private static readonly Guid Games = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);
    private static readonly DateRange Saturday = new(new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6));

    [Fact]
    public void Two_castles_give_the_cheapest_the_combo_discount()
    {
        var rule = Combo("2e springkasteel 40% korting", minimum: 2, percentage: 40, categories: [Castles]);

        var result = Calculate([Item(150, Castles), Item(120, Castles)], [rule]);

        var discount = Assert.Single(result.Discounts);
        Assert.Same(rule, discount.Rule);
        Assert.Equal(48m, discount.Amount);
        Assert.Equal(270m - 48m, result.Total);
    }

    [Fact]
    public void Three_castles_with_minimum_two_are_one_full_group()
    {
        var rule = Combo("2e springkasteel 40% korting", minimum: 2, percentage: 40, categories: [Castles]);

        var result = Calculate([Item(150, Castles), Item(100, Castles), Item(120, Castles)], [rule]);

        Assert.Equal((40m, 1), (Assert.Single(result.Discounts).Amount, result.Discounts[0].Pieces));
        Assert.Equal([0m, 40m, 0m], result.Pieces.Select(p => p.Discount));
    }

    [Fact]
    public void Four_castles_with_minimum_two_give_the_two_cheapest_the_discount()
    {
        var rule = Combo("2e springkasteel 50% korting", minimum: 2, percentage: 50, categories: [Castles]);

        // 2 × 150 op één lijn, 100 en 120 los: de stukken van 100 en 120 zijn de goedkoopste.
        var result = Calculate([Item(150, Castles, quantity: 2), Item(100, Castles), Item(120, Castles)], [rule]);

        Assert.Equal((110m, 2), (Assert.Single(result.Discounts).Amount, result.Discounts[0].Pieces));
        Assert.Equal(520m, result.Subtotal);
        Assert.Equal(410m, result.Total);
    }

    [Fact]
    public void Only_items_in_the_scope_of_the_rule_count_and_get_the_discount()
    {
        var rule = Combo("2e springkasteel 40% korting", minimum: 2, percentage: 40, categories: [Castles]);

        // Het goedkope spel telt niet mee: maar één springkasteel, geen volledige groep.
        var alone = Calculate([Item(150, Castles), Item(20, Games)], [rule]);
        Assert.Empty(alone.Discounts);

        // Twee springkastelen: de korting gaat naar het goedkoopste springkasteel, niet naar het spel.
        var two = Calculate([Item(150, Castles), Item(20, Games), Item(130, Castles)], [rule]);
        Assert.Equal(52m, Assert.Single(two.Discounts).Amount);
        Assert.Equal([0m, 0m, 52m], two.Pieces.Select(p => p.Discount));
    }

    [Fact]
    public void Chosen_items_count_together_whatever_their_category()
    {
        var pirate = CatalogRef.Item(Guid.NewGuid());
        var jungle = CatalogRef.Item(Guid.NewGuid());
        var rule = Combo("Piraat + Jungle", minimum: 2, percentage: 25, items: [pirate.RentalItemId!.Value, jungle.RentalItemId!.Value]);

        var result = Calculate([Line(pirate, 100, Castles), Line(jungle, 80, Games)], [rule]);

        Assert.Equal(20m, Assert.Single(result.Discounts).Amount);
    }

    [Fact]
    public void A_bundle_counts_as_one_piece_only_when_the_rule_names_it()
    {
        var party = CatalogRef.Bundle(Guid.NewGuid());
        var inCategory = Combo("2e springkasteel 40% korting", minimum: 2, percentage: 40, categories: [Castles]);
        var named = Combo("Feestpakket + springkasteel", minimum: 2, percentage: 40, categories: [Castles], bundles: [party.BundleId!.Value]);

        // Een pakket heeft geen categorie: de regel op de categorie springkastelen telt het niet.
        Assert.Empty(Calculate([Line(party, 90), Item(150, Castles)], [inCategory]).Discounts);

        // Staat het pakket in de regel, dan telt het als één stuk en krijgt het als goedkoopste de korting.
        var result = Calculate([Line(party, 90), Item(150, Castles)], [named]);
        Assert.Equal(36m, Assert.Single(result.Discounts).Amount);
        Assert.Equal([36m, 0m], result.Pieces.Select(p => p.Discount));
    }

    [Fact]
    public void The_items_inside_a_bundle_never_count_separately()
    {
        var castle = Guid.NewGuid();
        var party = CatalogRef.Bundle(Guid.NewGuid());
        var rule = Combo("2 x Piraat", minimum: 2, percentage: 40, items: [castle]);

        // Het pakket bevat het springkasteel, maar de prijsberekening ziet enkel het pakket als stuk.
        var result = Calculate([Line(party, 200), Line(CatalogRef.Item(castle), 150, Castles)], [rule]);

        Assert.Empty(result.Discounts);
    }

    [Fact]
    public void Discounts_are_rounded_to_the_cent()
    {
        var rule = Combo("Derde aan 33,33%", minimum: 2, percentage: 33.33m, categories: [Castles]);

        // 33,33% van 99,99 is 33,326667: afgerond 33,33.
        var result = Calculate([Item(150, Castles), Item(99.99m, Castles)], [rule]);

        Assert.Equal(33.33m, Assert.Single(result.Discounts).Amount);
        Assert.Equal(216.66m, result.Total);
    }

    [Fact]
    public void A_rule_that_is_switched_off_or_expired_does_not_count()
    {
        var off = Combo("Uit", minimum: 2, percentage: 40, categories: [Castles]);
        off.IsActive = false;
        var expired = Combo("Verlopen", minimum: 2, percentage: 40, categories: [Castles]);
        expired.EndDate = Today.AddDays(-1);
        var lastDay = Combo("Laatste dag", minimum: 2, percentage: 10, categories: [Castles]);
        lastDay.EndDate = Today;

        var result = Calculate([Item(150, Castles), Item(120, Castles)], [off, expired, lastDay]);

        Assert.Same(lastDay, Assert.Single(result.Discounts).Rule);
        Assert.Equal(12m, result.Discounts[0].Amount);
    }

    [Fact]
    public void A_piece_gets_at_most_one_discount_the_most_favourable()
    {
        var small = Combo("2e springkasteel 10% korting", minimum: 2, percentage: 10, categories: [Castles]);
        var big = Combo("2e springkasteel 30% korting", minimum: 2, percentage: 30, categories: [Castles]);

        var result = Calculate([Item(150, Castles), Item(100, Castles)], [small, big]);

        Assert.Same(big, Assert.Single(result.Discounts).Rule);
        Assert.Equal(30m, result.Discounts[0].Amount);
    }

    [Fact]
    public void Without_rules_the_price_is_the_normal_price_for_the_rental_period()
    {
        var weekend = new DateRange(new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 8));

        var result = Calculate([Item(150, Castles, quantity: 2, extraDayPrice: 75)], [], weekend);

        Assert.Equal(300m, Assert.Single(result.Lines).UnitPrice);
        Assert.Equal(600m, result.Total);
        Assert.Empty(result.Discounts);
    }

    [Fact]
    public void A_period_price_on_saturday_only_makes_the_saturday_more_expensive()
    {
        // Vrijdag tot zondag: vrijdag 150 (dagprijs), zaterdag 75 + 20% = 90, zondag 75.
        var friday = new DateRange(new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 7));
        var rule = Period("Zaterdag", 20, weekdays: [DayOfWeek.Saturday]);

        var result = Calculate([Item(150, Castles, extraDayPrice: 75)], [rule], friday);

        Assert.Equal(315m, Assert.Single(result.Lines).UnitPrice);
        Assert.Empty(result.Discounts);
    }

    [Fact]
    public void A_period_price_counts_only_the_days_within_its_dates()
    {
        // Huurperiode 30 juni tot 2 juli, zomer vanaf 1 juli +10%: 100 + 50 × 1,10 + 50 × 1,10 = 210.
        var period = new DateRange(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 2));
        var summer = Period("Zomer", 10, from: new DateOnly(2026, 7, 1), until: new DateOnly(2026, 8, 31));

        var result = Calculate([Item(100, Castles, extraDayPrice: 50)], [summer], period);

        Assert.Equal(210m, Assert.Single(result.Lines).UnitPrice);
    }

    [Fact]
    public void Weekdays_and_dates_together_count_only_the_weekdays_within_the_dates()
    {
        // Zaterdagen in juli +50%: zaterdag 27 juni telt niet, zaterdag 4 juli wel.
        var julySaturdays = Period("Zaterdagen in juli", 50, weekdays: [DayOfWeek.Saturday],
            from: new DateOnly(2026, 7, 1), until: new DateOnly(2026, 7, 31));
        var june = new DateRange(new DateOnly(2026, 6, 27), new DateOnly(2026, 6, 27));
        var july = new DateRange(new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 4));
        var julyFriday = new DateRange(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 3));

        Assert.Equal(100m, Calculate([Item(100, Castles)], [julySaturdays], june).Lines[0].UnitPrice);
        Assert.Equal(150m, Calculate([Item(100, Castles)], [julySaturdays], july).Lines[0].UnitPrice);
        Assert.Equal(100m, Calculate([Item(100, Castles)], [julySaturdays], julyFriday).Lines[0].UnitPrice);
    }

    [Fact]
    public void A_period_price_for_a_category_leaves_other_items_and_bundles_alone()
    {
        var castlesOnly = Period("Springkastelen op zaterdag", 20, categories: [Castles]);
        var party = CatalogRef.Bundle(Guid.NewGuid());

        var result = Calculate([Item(150, Castles), Item(20, Games), Line(party, 200)], [castlesOnly]);

        Assert.Equal([180m, 20m, 200m], result.Lines.Select(l => l.UnitPrice));
    }

    [Fact]
    public void A_period_price_for_everything_also_covers_bundles()
    {
        var party = CatalogRef.Bundle(Guid.NewGuid());

        var result = Calculate([Line(party, 200)], [Period("Zaterdag", 20, weekdays: [DayOfWeek.Saturday])]);

        Assert.Equal(240m, Assert.Single(result.Lines).UnitPrice);
    }

    [Fact]
    public void Two_period_prices_on_the_same_day_add_up()
    {
        // Zomer +10% en zaterdag +20% = +30% op zaterdag.
        var summer = Period("Zomer", 10, from: new DateOnly(2026, 6, 1), until: new DateOnly(2026, 8, 31));
        var saturday = Period("Zaterdag", 20, weekdays: [DayOfWeek.Saturday]);

        var result = Calculate([Item(100, Castles)], [summer, saturday]);

        Assert.Equal(130m, Assert.Single(result.Lines).UnitPrice);
        Assert.Empty(result.Discounts);
    }

    [Fact]
    public void A_cheaper_period_price_lowers_the_price_but_never_below_nothing()
    {
        var quiet = Period("Rustig", -30);
        Assert.Equal(70m, Calculate([Item(100, Castles)], [quiet]).Lines[0].UnitPrice);

        var veryQuiet = Period("Heel rustig", -90);
        Assert.Equal(0m, Calculate([Item(100, Castles)], [quiet, veryQuiet]).Lines[0].UnitPrice);
    }

    [Fact]
    public void A_period_price_that_is_switched_off_or_expired_does_not_count()
    {
        var off = Period("Uit", 20);
        off.IsActive = false;
        var expired = Period("Verlopen", 20);
        expired.EndDate = Today.AddDays(-1);

        Assert.Equal(100m, Calculate([Item(100, Castles)], [off, expired]).Lines[0].UnitPrice);
    }

    [Fact]
    public void A_discount_computes_on_the_price_after_the_period_price()
    {
        var saturday = Period("Zaterdag", 20, weekdays: [DayOfWeek.Saturday]);
        var combo = Combo("2e springkasteel 50% korting", minimum: 2, percentage: 50, categories: [Castles]);

        // 150 → 180 en 100 → 120; 50% van 120 = 60.
        var result = Calculate([Item(150, Castles), Item(100, Castles)], [saturday, combo]);

        Assert.Equal(300m, result.Subtotal);
        Assert.Equal(60m, Assert.Single(result.Discounts).Amount);
        Assert.Equal(240m, result.Total);
    }

    [Fact]
    public void Each_day_with_a_period_price_is_rounded_to_the_cent()
    {
        // 0,05 met +10% = 0,055 → 0,06 per dag; twee dagen samen 0,12 (het totaal afronden zou 0,11 geven).
        var twoDays = new DateRange(new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 7));

        var result = Calculate([Item(0.05m, Castles)], [Period("Iets duurder", 10)], twoDays);

        Assert.Equal(0.12m, Assert.Single(result.Lines).UnitPrice);
    }

    private static PriceResult Calculate(IReadOnlyList<PriceLine> lines, IReadOnlyList<PricingRule> rules, DateRange? period = null) =>
        PriceCalculator.Calculate(new PriceRequest(lines, period ?? Saturday, Today, rules));

    private static PriceLine Item(decimal dayPrice, Guid? category = null, int quantity = 1, decimal? extraDayPrice = null) =>
        new(CatalogRef.Item(Guid.NewGuid()), category, new Pricing { DayPrice = dayPrice, ExtraDayPrice = extraDayPrice }, quantity);

    private static PriceLine Line(CatalogRef entry, decimal dayPrice, Guid? category = null) =>
        new(entry, category, new Pricing { DayPrice = dayPrice }, 1);

    private static PricingRule Combo(string name, int minimum, decimal percentage, Guid[]? categories = null, Guid[]? items = null, Guid[]? bundles = null) => new()
    {
        Kind = PricingRuleKind.Combo,
        Names = [new Translation { Culture = "nl", Name = name }],
        MinimumQuantity = minimum,
        Percentage = percentage,
        CategoryIds = [.. categories ?? []],
        RentalItemIds = [.. items ?? []],
        BundleIds = [.. bundles ?? []],
    };

    private static PricingRule Period(string name, decimal percentage, DayOfWeek[]? weekdays = null, DateOnly? from = null, DateOnly? until = null,
        Guid[]? categories = null, Guid[]? items = null, Guid[]? bundles = null) => new()
    {
        Kind = PricingRuleKind.Period,
        Names = [new Translation { Culture = "nl", Name = name }],
        Percentage = percentage,
        Weekdays = [.. weekdays ?? []],
        PeriodStart = from,
        PeriodEnd = until,
        AppliesToAll = categories is null && items is null && bundles is null,
        CategoryIds = [.. categories ?? []],
        RentalItemIds = [.. items ?? []],
        BundleIds = [.. bundles ?? []],
    };
}
