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

    /// <summary>De combinatiekortingen die vandaag gelden voor dit artikel of pakket, voor de zin op de artikelpagina.</summary>
    public IReadOnlyList<PricingRule> CombosFor(CatalogEntry entry) =>
        Rules.Where(r => r.Kind == PricingRuleKind.Combo && r.IsInForceOn(ReservationDate) && r.Covers(entry.Ref(), entry.CategoryId)).ToList();
}

public static class PriceLines
{
    /// <summary>Een artikel of pakket van de reservatiewebsite als lijn voor de prijsberekening.</summary>
    public static PriceLine ToPriceLine(this CatalogEntry entry, int quantity) =>
        new(entry.Ref(), entry.Kind == CatalogEntryKind.Item ? entry.CategoryId : null,
            new Pricing { DayPrice = entry.DayPrice, ExtraDayPrice = entry.ExtraDayPrice }, quantity);

    public static CatalogRef Ref(this CatalogEntry entry) =>
        entry.Kind == CatalogEntryKind.Item ? CatalogRef.Item(entry.Id) : CatalogRef.Bundle(entry.Id);
}
