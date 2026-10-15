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

/// <summary>Een lijn met haar prijs per stuk voor de huurperiode, na periodeprijzen en voor kortingen.</summary>
public sealed record PricedLine(PriceLine Line, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Line.Quantity;
}

/// <summary>
/// Eén stuk van een lijn (<see cref="LineIndex"/> in de gevraagde lijnen) met zijn prijs en de korting die het kreeg,
/// of geen (<see cref="Rule"/> null, <see cref="Discount"/> 0). Elk stuk krijgt hoogstens één korting. Voor de rekentool.
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
/// actie krijgt, is niet het goedkoopste stuk van een groep voor een combinatiekorting.</item>
/// </list>
/// </summary>
public static class PriceCalculator
{
    public static PriceResult Calculate(PriceRequest request)
    {
        var rules = request.Rules.Where(r => r.IsInForceOn(request.ReservationDate)).ToList();
        var lines = request.Lines.Select(l => new PricedLine(l, UnitPrice(l, request.Period, rules))).ToList();
        var pieces = lines.SelectMany((l, index) => Enumerable.Repeat(new Piece(index, l.Line, l.UnitPrice), l.Line.Quantity)).ToList();

        var candidates = pieces.Select(_ => new List<Candidate>()).ToArray();
        var promotions = BestPromotions(pieces, request, rules);
        for (var i = 0; i < pieces.Count; i++)
            if (promotions[i] is { } promotion) candidates[i].Add(promotion);
        foreach (var rule in rules.Where(r => r.Kind == PricingRuleKind.Combo))
            AddComboCandidates(rule, pieces, promotions, candidates);

        // Per stuk de voordeligste korting; bij gelijke korting de regel die eerst kwam.
        var best = candidates.Select(c => c.OrderByDescending(x => x.Amount).FirstOrDefault()).ToList();
        var priced = pieces.Select((p, i) => new PricedPiece(p.LineIndex, p.Price, best[i]?.Rule, best[i]?.Amount ?? 0)).ToList();
        var discounts = priced.Where(p => p.Rule is not null && p.Discount > 0)
            .GroupBy(p => p.Rule!)
            .Select(g => new PriceDiscount(g.Key, g.Sum(p => p.Discount), g.Count()))
            .ToList();
        return new PriceResult(lines, priced, discounts);
    }

    /// <summary>De prijs per stuk voor de huurperiode, dag per dag, met de periodeprijzen van elke dag, op de cent.</summary>
    private static decimal UnitPrice(PriceLine line, DateRange period, IReadOnlyList<PricingRule> rules)
    {
        var total = 0m;
        foreach (var day in period.EachDay())
        {
            var price = day == period.Start ? line.Pricing.DayPrice : line.Pricing.ExtraDayPrice ?? line.Pricing.DayPrice;
            total += Round(price * (100 + DayPercentage(line, day, rules)) / 100);
        }
        return total;
    }

    /// <summary>
    /// Hoeveel procent duurder (of goedkoper) deze dag is voor deze lijn: de som van de periodeprijzen die op die dag
    /// gelden. Periodeprijzen komen er nog bij (#92); tot dan geldt elke dag de gewone prijs.
    /// </summary>
    private static decimal DayPercentage(PriceLine line, DateOnly day, IReadOnlyList<PricingRule> rules) => 0;

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
    /// minimum aantal geeft zijn goedkoopste stuk de korting, dus de goedkoopste (aantal / minimum) stuks. Een stuk met
    /// een actie die minstens even goed is, houdt zijn actie: het telt mee voor de groepen, maar niet als goedkoopste,
    /// en de korting gaat naar het volgende goedkoopste stuk.
    /// </summary>
    private static void AddComboCandidates(PricingRule rule, IReadOnlyList<Piece> pieces, Candidate?[] promotions, List<Candidate>[] candidates)
    {
        if (rule.MinimumQuantity is not { } minimum || minimum < 2) return;
        var inScope = pieces.Select((p, i) => (Piece: p, Index: i))
            .Where(x => rule.Covers(x.Piece.Line.Entry, x.Piece.Line.CategoryId))
            .OrderBy(x => x.Piece.Price) // stabiel: bij gelijke prijs de volgorde van de lijnen
            .ToList();
        var receivers = inScope
            .Select(x => (x.Index, Discount: Percent(x.Piece.Price, rule.Percentage)))
            .Where(x => (promotions[x.Index]?.Amount ?? 0) < x.Discount)
            .Take(inScope.Count / minimum);
        foreach (var (index, discount) in receivers)
            candidates[index].Add(new Candidate(rule, discount));
    }

    private static decimal Percent(decimal price, decimal percentage) => Round(price * percentage / 100);

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record Piece(int LineIndex, PriceLine Line, decimal Price);

    private sealed record Candidate(PricingRule Rule, decimal Amount);
}
