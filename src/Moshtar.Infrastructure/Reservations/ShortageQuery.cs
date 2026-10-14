using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>
/// De tekorten van de verhuurder vanaf vandaag, met <see cref="ShortageCalculator"/>: bevestigde reservaties (pakketten
/// opengeklapt, met bufferdagen) tegenover de voorraad min de blokkeringen. Ook uitgezette artikelen tellen mee: hun
/// bestaande reservaties blijven staan. Elke berekening heeft een eigen context, zodat ze in het back office naast een
/// scherm kan lopen (de teller in het menu).
/// </summary>
internal sealed class ShortageQuery(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock)
{
    /// <param name="rentalItemId">Alleen de tekorten van dit artikel, of null voor alle artikelen.</param>
    public async Task<IReadOnlyList<Shortage>> FindAsync(Guid? rentalItemId = null, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Een reservatie die al voorbij is, kan met haar bufferdagen na nog tot vandaag lopen.
        var earliestEnd = today.AddDays(-tenant.BufferDaysAfter);
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => Reservation.StockOccupyingStatuses.Contains(r.Status) && r.EndDate >= earliestEnd)
            .ToListAsync(ct);
        var bundleIds = reservations.SelectMany(r => r.Lines).Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var itemIds = reservations
            .SelectMany(r => DemandExpander.Expand(r, bundles))
            .Select(r => r.Demand.RentalItemId)
            .Where(id => rentalItemId is null || id == rentalItemId)
            .Distinct().ToList();
        if (itemIds.Count == 0) return [];

        var stock = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Stock, ct);
        var blocked = (await db.Blockouts.AsNoTracking()
                .Where(b => itemIds.Contains(b.RentalItemId) && (b.EndDate == null || b.EndDate >= today)).ToListAsync(ct))
            .Select(b => b.Demand);

        return ShortageCalculator.Find(stock, reservations, bundles, blocked, today, tenant.BufferDaysBefore, tenant.BufferDaysAfter)
            .Where(s => rentalItemId is null || s.RentalItemId == rentalItemId)
            .ToList();
    }
}
