using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Prices;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;
using Moshtar.Infrastructure.Catalog;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Website;

namespace Moshtar.Infrastructure.Prices;

/// <summary>
/// Met een eigen, kortlevende context per oproep: de rekentool laadt terwijl een tabblad van de pagina Prijsregels de
/// gedeelde context gebruikt. Leest de artikelen en pakketten zoals de winkelkar (de catalogus van de reservatiewebsite)
/// en prijst ze met dezelfde <see cref="PriceQuoter"/>.
/// </summary>
internal sealed class PriceExamples(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock) : IPriceExamples
{
    private string Culture => tenantContext.Tenant?.DefaultCulture ?? "nl";

    public async Task<PriceExampleChoices> GetChoicesAsync(CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return new PriceExampleChoices(Belgium.Today(clock), await Catalog(db).GetCatalogAsync(Culture, ct));
    }

    public async Task<PriceExampleResult> CalculateAsync(PriceExample example, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entries = (await Catalog(db).GetCatalogAsync(Culture, ct)).ToDictionary(e => e.Ref());
        var chosen = example.Lines.Where(l => l.Quantity > 0 && entries.ContainsKey(l.Entry)).Select(l => (Entry: entries[l.Entry], l.Quantity)).ToList();

        // Alle regels die aan staan, met de gekozen reservatiedatum: wat dan verlopen is of nog niet loopt, beslist de berekening.
        var current = await PriceQuotes.CurrentAsync(db, clock, ct);
        var result = new PriceQuoter(example.ReservationDate, current.Rules)
            .Quote(example.Period, chosen.Select(c => c.Entry.ToPriceLine(c.Quantity)).ToList());

        var lines = result.Lines.Select((line, index) => new PriceExampleLineResult(
                chosen[index].Entry.Name, line.Line.Quantity, chosen[index].Entry.PriceFor(example.Period.Days), line.UnitPrice, line.Days,
                result.Pieces.Where(p => p.LineIndex == index).Select(p => new PriceExamplePiece(p.Price, p.Rule is { } r ? NameOf(r) : null, p.Discount)).ToList()))
            .ToList();
        var discounts = result.Discounts.Select(d => new PriceExampleDiscount(NameOf(d.Rule), d.Amount, d.Pieces)).ToList();
        return new PriceExampleResult(lines, discounts, result.Subtotal, result.Total);
    }

    /// <summary>De naam van de regel in de standaardtaal; een periodeprijs heeft een naam alleen voor het back office.</summary>
    private string NameOf(PricingRule rule) => rule.NameIn(Culture, Culture);

    // Met de hand op de eigen context: ICatalogService uit DI deelt de scoped context, en een nieuwe DI-scope kent de verhuurder niet.
    private CatalogService Catalog(AppDbContext db) => new(db, tenantContext, new WebsiteTextAdministration(db, tenantContext, clock));
}
