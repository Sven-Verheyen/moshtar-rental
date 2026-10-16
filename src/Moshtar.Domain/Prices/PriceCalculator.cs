using Moshtar.Domain.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Prices;

/// <summary>
/// Een lijn om te prijzen: een artikel of pakket, zijn categorie (alleen voor een artikel), zijn gewone prijzen en hoeveel stuks.
/// </summary>
public sealed record PriceLine(CatalogRef Entry, Guid? CategoryId, Pricing Pricing, int Quantity);

/// <summary>
/// Wat de prijsberekening nodig heeft: de lijnen, de huurperiode, de dag waarop gereserveerd wordt (vandaag, voor
/// acties en einddata) en de prijsregels van de verhuurder. Regels die uit staan of verlopen zijn, tellen niet mee.
/// </summary>
public sealed record PriceRequest(IReadOnlyList<PriceLine> Lines, DateRange Period, DateOnly ReservationDate, IReadOnlyList<PricingRule> Rules);

/// <summary>
/// Een lijn met haar prijs per stuk voor de huurperiode, na periodeprijzen en voor kortingen, en hoe die prijs dag per
/// dag tot stand kwam (<see cref="Days"/>, voor de rekentool).
/// </summary>
public sealed record PricedLine(PriceLine Line, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Line.Quantity;

    /// <summary>Elke dag van de huurperiode met zijn prijs per stuk; samen <see cref="UnitPrice"/>.</summary>
    public IReadOnlyList<PricedDay> Days { get; init; } = [];
}

/// <summary>
/// Eén dag van de huurperiode voor één stuk: de gewone prijs van die dag (dagprijs op dag 1, anders prijs per extra
/// dag), de som van de periodeprijzen die dan gelden in procent, en de prijs daarmee, op de cent afgerond.
/// </summary>
public sealed record PricedDay(DateOnly Date, decimal NormalPrice, decimal Percentage, decimal Price);

/// <summary>
/// Eén stuk van een lijn (<see cref="LineIndex"/> in de gevraagde lijnen) met zijn prijs en de korting die het kreeg,
/// of geen (<see cref="Rule"/> null, <see cref="Discount"/> 0). Elk stuk krijgt hoogstens één korting. Een stuk van 0 kan
/// de plaats van goedkoopste in een combinatiekorting innemen met een korting van 0 (<see cref="Rule"/> gezet,
/// <see cref="Discount"/> 0). Voor de rekentool.
/// </summary>
public sealed record PricedPiece(int LineIndex, decimal Price, PricingRule? Rule, decimal Discount);

/// <summary>Een kortingslijn: alles wat één regel samen van het totaal aftrekt (positief bedrag), over zoveel stuks.</summary>
public sealed record PriceDiscount(PricingRule Rule, decimal Amount, int Pieces);

/// <summary>De uitkomst: de lijnen met hun prijs per stuk, elk stuk met zijn korting, en een kortingslijn per regel.</summary>
public sealed record PriceResult(IReadOnlyList<PricedLine> Lines, IReadOnlyList<PricedPiece> Pieces, IReadOnlyList<PriceDiscount> Discounts)
{
    /// <summary>Het totaal van de lijnen, zonder kortingen.</summary>
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    /// <summary>Wat de klant betaalt: de lijnen min de kortingen.</summary>
    public decimal Total => Subtotal - Discounts.Sum(d => d.Amount);
}

/// <summary>
/// De ene prijsberekening van Moshtar, voor de reservatiewebsite, de winkelkar, reserveren (website en back office),
/// Open winkelkarren, de herinneringsmail, GA4 en de rekentool. In deze volgorde:
/// <list type="number">
/// <item>de prijs per stuk: per dag van de huurperiode (dag 1 aan dagprijs, de andere aan prijs per extra dag), maal de
/// periodeprijzen van die dag, op de cent afgerond;</item>
/// <item>per stuk de kandidaat-kortingen van de regels (combinatiekortingen, acties), elk in procent van die prijs en op
/// de cent afgerond;</item>
/// <item>per stuk hoogstens één korting: de voordeligste. Van twee acties geldt de hoogste; een stuk dat een betere
/// actie of hogere combinatiekorting krijgt, is niet het goedkoopste stuk van een groep voor een (andere)
/// combinatiekorting.</item>
/// </list>
/// </summary>
public static class PriceCalculator
{
    public static PriceResult Calculate(PriceRequest request)
    {
        var rules = request.Rules.Where(r => r.IsInForceOn(request.ReservationDate)).ToList();
        var lines = request.Lines.Select(l => Price(l, request.Period, rules)).ToList();
        var pieces = lines.SelectMany((l, index) => Enumerable.Repeat(new Piece(index, l.Line, l.UnitPrice), l.Line.Quantity)).ToList();

        // Per stuk de voordeligste korting: eerst de beste actie, dan de combinatiekortingen van hoog naar laag
        // percentage (bij gelijk percentage de regel die eerst kwam). Elke combinatiekorting slaat de stuks over die al
        // een minstens even goede korting hebben, dus een lagere krijgt nooit de stuks van een hogere.
        var best = BestPromotions(pieces, request, rules);
        foreach (var rule in rules.Where(r => r.Kind == PricingRuleKind.Combo).OrderByDescending(r => r.Percentage))
            AddComboDiscount(rule, pieces, best);
        var priced = pieces.Select((p, i) => new PricedPiece(p.LineIndex, p.Price, best[i]?.Rule, best[i]?.Amount ?? 0)).ToList();
        var discounts = priced.Where(p => p.Rule is not null && p.Discount > 0)
            .GroupBy(p => p.Rule!)
            .Select(g => new PriceDiscount(g.Key, g.Sum(p => p.Discount), g.Count()))
            .ToList();
        return new PriceResult(lines, priced, discounts);
    }

    /// <summary>De prijs per stuk voor elke dag van de huurperiode, met de periodeprijzen van die dag, op de cent.</summary>
    private static PricedLine Price(PriceLine line, DateRange period, IReadOnlyList<PricingRule> rules)
    {
        var days = new List<PricedDay>();
        foreach (var day in period.EachDay())
        {
            var price = day == period.Start ? line.Pricing.DayPrice : line.Pricing.ExtraDayPrice ?? line.Pricing.DayPrice;
            var percentage = DayPercentage(line, day, rules);
            days.Add(new PricedDay(day, price, percentage, Round(price * (100 + percentage) / 100)));
        }
        return new PricedLine(line, days.Sum(d => d.Price)) { Days = days };
    }

    /// <summary>
    /// Hoeveel procent duurder (of goedkoper) deze dag is voor deze lijn: de som van de periodeprijzen die op die dag
    /// gelden (zie <see cref="PeriodPrices"/>).
    /// </summary>
    private static decimal DayPercentage(PriceLine line, DateOnly day, IReadOnlyList<PricingRule> rules) =>
        PeriodPrices.DayPercentage(line, day, rules);

    /// <summary>
    /// Per stuk de hoogste actie die geldt: op de reservatiedatum binnen haar reservatiedata, de huurperiode binnen haar
    /// huurperiodebereik, en het stuk in haar bereik. Bij gelijke korting de actie die eerst kwam.
    /// </summary>
    private static Candidate?[] BestPromotions(IReadOnlyList<Piece> pieces, PriceRequest request, IReadOnlyList<PricingRule> rules)
    {
        var promotions = rules.Where(r => r.IsPromotionOn(request.ReservationDate) && r.AllowsRentalPeriod(request.Period)).ToList();
        return pieces
            .Select(p => promotions
                .Where(r => r.Covers(p.Line.Entry, p.Line.CategoryId))
                .Select(r => new Candidate(r, Percent(p.Price, r.Percentage)))
                .OrderByDescending(c => c.Amount)
                .FirstOrDefault())
            .ToArray();
    }

    /// <summary>
    /// Een combinatiekorting: de stuks in het bereik van de regel van goedkoop naar duur; elke volledige groep van het
    /// minimum aantal geeft zijn goedkoopste stuk de korting, dus de goedkoopste (aantal / minimum) stuks. Een stuk dat
    /// al een minstens even goede korting heeft (een actie of een hogere combinatiekorting), houdt die: het telt mee
    /// voor de groepen, maar niet als goedkoopste, en de korting gaat naar het volgende goedkoopste stuk.
    /// </summary>
    private static void AddComboDiscount(PricingRule rule, IReadOnlyList<Piece> pieces, Candidate?[] best)
    {
        if (rule.MinimumQuantity is not { } minimum || minimum < 2) return;
        var inScope = pieces.Select((p, i) => (Piece: p, Index: i))
            .Where(x => rule.Covers(x.Piece.Line.Entry, x.Piece.Line.CategoryId))
            .OrderBy(x => x.Piece.Price) // stabiel: bij gelijke prijs de volgorde van de lijnen
            .ToList();
        var receivers = inScope
            .Select(x => (x.Index, Discount: Percent(x.Piece.Price, rule.Percentage)))
            // Geen korting is iets anders dan een korting van 0: een stuk van 0 (of met een korting die op 0 afrondt)
            // zonder korting blijft het goedkoopste stuk en neemt de plaats van de groep in.
            .Where(x => best[x.Index] is not { } current || current.Amount < x.Discount)
            .Take(inScope.Count / minimum)
            .ToList();
        foreach (var (index, discount) in receivers)
            best[index] = new Candidate(rule, discount);
    }

    private static decimal Percent(decimal price, decimal percentage) => Round(price * percentage / 100);

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record Piece(int LineIndex, PriceLine Line, decimal Price);

    private sealed record Candidate(PricingRule Rule, decimal Amount);
}
