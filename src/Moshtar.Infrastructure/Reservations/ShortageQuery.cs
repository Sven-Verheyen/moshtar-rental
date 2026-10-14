using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>
/// De tekorten van de verhuurder vanaf vandaag, met <see cref="ShortageCalculator"/>: bevestigde reservaties (pakketten
/// opengeklapt, met bufferdagen) tegenover de voorraad min de blokkeringen. Ook uitgezette artikelen tellen mee: hun
/// bestaande reservaties blijven staan.
/// </summary>
internal sealed class ShortageQuery(AppDbContext db, ITenantContext tenantContext, TimeProvider clock)
{
    /// <param name="rentalItemId">Alleen de tekorten van dit artikel, of null voor alle artikelen.</param>
    public async Task<IReadOnlyList<Shortage>> FindAsync(Guid? rentalItemId = null, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Een reservatie die al voorbij is, kan met haar bufferdagen na nog tot vandaag lopen.
        var earliestEnd = today.AddDays(-tenant.BufferDaysAfter);
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => ReservationService.OccupyingStatuses.Contains(r.Status) && r.EndDate >= earliestEnd)
            .ToListAsync(ct);
        var bundleIds = reservations.SelectMany(r => r.Lines).Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var reserved = reservations
            .SelectMany(r => DemandExpander.Expand(r.Lines, r.Period, bundles).Select(d => new ReservedDemand(r.Id, d)))
            .Where(r => rentalItemId is null || r.Demand.RentalItemId == rentalItemId)
            .ToList();
        if (reserved.Count == 0) return [];

        var itemIds = reserved.Select(r => r.Demand.RentalItemId).Distinct().ToList();
        var stock = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Stock, ct);
        var blocked = (await db.Blockouts.AsNoTracking().Where(b => itemIds.Contains(b.RentalItemId) && b.EndDate >= today).ToListAsync(ct))
            .Select(b => new StockDemand(b.RentalItemId, b.Quantity, new DateRange(b.StartDate, b.EndDate)));

        return ShortageCalculator.Find(stock, reserved, blocked, today, tenant.BufferDaysBefore, tenant.BufferDaysAfter);
    }
}
