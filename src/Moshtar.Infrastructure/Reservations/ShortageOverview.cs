using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>
/// De lijst Tekorten: de tekorten uit <see cref="ShortageQuery"/>, per artikel gegroepeerd en aangevuld met de naam
/// van het artikel en het nummer, de klant en de huurperiode van elke getroffen reservatie.
/// </summary>
internal sealed class ShortageOverview(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, ShortageQuery shortages)
    : IShortageOverview
{
    public async Task<IReadOnlyList<ItemShortages>> GetAsync(CancellationToken ct = default)
    {
        var found = await shortages.FindAsync(ct: ct);
        if (found.Count == 0) return [];

        var culture = tenantContext.Tenant?.DefaultCulture ?? "nl";
        await using var db = await contexts.CreateDbContextAsync(ct);
        var itemIds = found.Select(s => s.RentalItemId).Distinct().ToList();
        var items = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var reservationIds = found.SelectMany(s => s.ReservationIds).Distinct().ToList();
        var reservations = await db.Reservations.AsNoTracking().Include(r => r.Customer)
            .Where(r => reservationIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        return found
            .GroupBy(s => s.RentalItemId)
            .Select(item => new ItemShortages(
                item.Key,
                items.TryGetValue(item.Key, out var rentalItem)
                    ? rentalItem.Translations.For(culture, culture)?.Name ?? rentalItem.Slug
                    : "(verwijderd artikel)",
                item.Select(s => new ShortageDay(s.Day, s.Rentable, s.Reserved)).ToList(),
                item.SelectMany(s => s.Reservations.Select(r => (s.Day, Affected: r)))
                    .GroupBy(a => a.Affected.ReservationId)
                    .Select(a =>
                    {
                        var reservation = reservations[a.Key];
                        var bundleIds = a.SelectMany(x => x.Affected.BundleIds).Distinct().ToList();
                        var bundleNames = reservation.Lines
                            .Where(l => l.BundleId is { } id && bundleIds.Contains(id))
                            .Select(l => l.Description).Distinct().ToList();
                        return new ShortageReservation(
                            reservation.Id,
                            reservation.Number,
                            $"{reservation.Customer?.FirstName} {reservation.Customer?.LastName}".Trim(),
                            reservation.Period,
                            a.Max(x => x.Affected.Quantity),
                            bundleNames.Count > 0 ? string.Join(", ", bundleNames) : null,
                            a.Select(x => x.Day).Order().ToList());
                    })
                    .OrderBy(r => r.Period.Start).ThenBy(r => r.Number)
                    .ToList()))
            .OrderBy(i => i.RentalItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        (await shortages.FindAsync(ct: ct))
            .SelectMany(s => s.ReservationIds.Select(r => (s.RentalItemId, r)))
            .Distinct()
            .Count();
}
