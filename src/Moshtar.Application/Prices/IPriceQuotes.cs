using Moshtar.Application.Catalog;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;

namespace Moshtar.Application.Prices;

/// <summary>
/// De prijsregels van de huidige verhuurder zoals ze vandaag gelden, om prijzen te berekenen op de reservatiewebsite,
/// in de winkelkar, Open winkelkarren en de herinneringsmail. Reserveren leest ze zelf, binnen zijn transactie.
/// </summary>
public interface IPriceQuotes
{
    Task<PriceQuoter> CurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// De prijsregels van een verhuurder met de reservatiedatum (vandaag): berekent prijzen met de ene
/// <see cref="PriceCalculator"/>. Eén keer laden, zo vaak gebruiken als nodig (bv. voor elke open winkelkar).
/// </summary>
public sealed class PriceQuoter(DateOnly reservationDate, IReadOnlyList<PricingRule> rules)
{
    public DateOnly ReservationDate { get; } = reservationDate;
    public IReadOnlyList<PricingRule> Rules { get; } = rules;

    public PriceResult Quote(DateRange period, IReadOnlyList<PriceLine> lines) =>
        PriceCalculator.Calculate(new PriceRequest(lines, period, ReservationDate, Rules));

    /// <summary>
    /// De prijs van één stuk van dit artikel of pakket voor de huurperiode, met de periodeprijzen van elke dag: wat de
    /// reservatiewebsite toont met een gekozen datum (zonder datum blijft de gewone prijs, <see cref="CatalogEntry.PriceFor"/>).
    /// </summary>
    public decimal PriceFor(CatalogEntry entry, DateRange period) => Quote(period, [entry.ToPriceLine(1)]).Lines[0].UnitPrice;

    /// <summary>
    /// De prijs van één stuk op de reservatiewebsite (artikelkaart, artikelpagina, structured data), met de actie die
    /// geldt. Met huurperiode: de prijs voor die periode. Zonder: de dagprijs, en alleen een actie die niet aan
    /// huurperiodes gebonden is (zonder datum weet de klant nog niet of ze voor hem geldt). Een combinatiekorting telt
    /// hier niet: die vraagt meerdere stuks.
    /// </summary>
    public SitePrice SitePriceFor(CatalogEntry entry, DateRange? period)
    {
        var line = entry.ToPriceLine(1);
        var result = period is { } p
            ? Quote(p, [line])
            // Eén dag aan dagprijs, alleen met de acties zonder huurperiodebereik.
            : PriceCalculator.Calculate(new PriceRequest([line], new DateRange(ReservationDate, ReservationDate), ReservationDate,
                Rules.Where(r => r.Kind == PricingRuleKind.Promotion && !r.IsBoundToRentalDates).ToList()));
        var normal = result.Lines[0].UnitPrice;
        return result.Pieces[0] is { Rule: { Kind: PricingRuleKind.Promotion } promotion, Discount: > 0 } piece
            ? new SitePrice(normal, normal - piece.Discount, promotion)
            : new SitePrice(normal, normal, null);
    }

    /// <summary>De combinatiekortingen die vandaag gelden voor dit artikel of pakket, voor de zin op de artikelpagina.</summary>
    public IReadOnlyList<PricingRule> CombosFor(CatalogEntry entry) =>
        Rules.Where(r => r.Kind == PricingRuleKind.Combo && r.IsInForceOn(ReservationDate) && r.Covers(entry.Ref(), entry.CategoryId)).ToList();
}

/// <summary>
/// Een prijs op de reservatiewebsite: <see cref="Normal"/> zonder korting, <see cref="Price"/> wat de klant betaalt, en
/// de actie die het verschil maakt (of null). Met een actie staat de gewone prijs doorstreept naast het label.
/// </summary>
public sealed record SitePrice(decimal Normal, decimal Price, PricingRule? Promotion);

public static class PriceLines
{
    /// <summary>Een artikel of pakket van de reservatiewebsite als lijn voor de prijsberekening.</summary>
    public static PriceLine ToPriceLine(this CatalogEntry entry, int quantity) =>
        new(entry.Ref(), entry.Kind == CatalogEntryKind.Item ? entry.CategoryId : null,
            new Pricing { DayPrice = entry.DayPrice, ExtraDayPrice = entry.ExtraDayPrice }, quantity);

    public static CatalogRef Ref(this CatalogEntry entry) =>
        entry.Kind == CatalogEntryKind.Item ? CatalogRef.Item(entry.Id) : CatalogRef.Bundle(entry.Id);
}
