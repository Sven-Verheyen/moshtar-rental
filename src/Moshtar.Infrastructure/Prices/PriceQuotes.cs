using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Prices;
using Moshtar.Domain.Prices;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Prices;

/// <summary>Met een eigen, kortlevende context per oproep, zoals de winkelkar.</summary>
internal sealed class PriceQuotes(IDbContextFactory<AppDbContext> contexts, TimeProvider clock) : IPriceQuotes
{
    public async Task<PriceQuoter> CurrentAsync(CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await CurrentAsync(db, clock, ct);
    }

    /// <summary>De prijsregels die aan staan, met vandaag (klok van de verhuurder) als reservatiedatum.</summary>
    internal static async Task<PriceQuoter> CurrentAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var rules = await db.PricingRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Id).ToListAsync(ct);
        return new PriceQuoter(today, rules);
    }
}
