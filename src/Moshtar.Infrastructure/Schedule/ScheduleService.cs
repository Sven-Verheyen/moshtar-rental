using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Schedule;

internal sealed class ScheduleService(AppDbContext db, ITenantContext tenantContext) : ISchedule
{
    /// <summary>Het back office is Nederlandstalig.</summary>
    private const string Culture = "nl";

    public async Task<ScheduleDay> GetDayAsync(DateOnly day, CancellationToken ct = default)
    {
        var reservations = await db.Reservations.AsNoTracking().Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled && (r.StartDate == day || r.EndDate == day))
            .ToListAsync(ct);
        var names = await NamesAsync(reservations, ct);

        var stops = reservations.Select(r => (Reservation: r, Stop: ToStop(r, names))).ToList();
        IReadOnlyList<ScheduleStop> Select(DeliveryMethod method, Func<Reservation, bool> onDay) => stops
            .Where(s => s.Reservation.DeliveryMethod == method && onDay(s.Reservation))
            .Select(s => s.Stop)
            .OrderBy(s => s.Address?.PostalCode).ThenBy(s => s.Address?.Street).ThenBy(s => s.Number)
            .ToList();

        return new ScheduleDay(
            day,
            Deliveries: Select(DeliveryMethod.Delivery, r => r.StartDate == day),
            Collections: Select(DeliveryMethod.Delivery, r => r.EndDate == day),
            Pickups: Select(DeliveryMethod.Pickup, r => r.StartDate == day),
            Returns: Select(DeliveryMethod.Pickup, r => r.EndDate == day));
    }

    private static ScheduleStop ToStop(Reservation r, CatalogNames names) => new(
        r.Id,
        r.Number,
        $"{r.Customer?.FirstName} {r.Customer?.LastName}".Trim(),
        string.IsNullOrWhiteSpace(r.Customer?.Phone) ? null : r.Customer.Phone,
        r.DeliveryMethod == DeliveryMethod.Delivery ? r.DeliveryAddress : null,
        r.Lines.Select(l => ToLine(l, names)).ToList(),
        string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes,
        r.Status,
        r.Period);

    private static ScheduleLine ToLine(ReservationLine line, CatalogNames names)
    {
        if (line.BundleId is { } bundleId && names.Bundles.TryGetValue(bundleId, out var bundle))
            return new ScheduleLine(bundle.Name, line.Quantity, bundle.Items
                .Select(i => new ScheduleLine(names.Items.GetValueOrDefault(i.RentalItemId, "?"), i.Quantity * line.Quantity, []))
                .ToList());
        var name = line.RentalItemId is { } itemId ? names.Items.GetValueOrDefault(itemId) : null;
        return new ScheduleLine(name ?? line.Description, line.Quantity, []);
    }

    /// <summary>De Nederlandse namen van de artikelen en pakketten op deze reservaties, ook van artikelen in een pakket.</summary>
    private async Task<CatalogNames> NamesAsync(IReadOnlyList<Reservation> reservations, CancellationToken ct)
    {
        var fallback = tenantContext.Tenant?.DefaultCulture ?? Culture;
        var bundleIds = reservations.SelectMany(r => r.Lines).Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToListAsync(ct);
        var itemIds = reservations.SelectMany(r => r.Lines).Select(l => l.RentalItemId).OfType<Guid>()
            .Concat(bundles.SelectMany(b => b.Items).Select(i => i.RentalItemId))
            .Distinct().ToList();
        var items = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);

        return new CatalogNames(
            items.ToDictionary(i => i.Id, i => i.Translations.For(Culture, fallback)?.Name ?? i.Slug),
            bundles.ToDictionary(b => b.Id, b => (b.Translations.For(Culture, fallback)?.Name ?? b.Slug, (IReadOnlyList<Domain.Catalog.BundleItem>)b.Items)));
    }

    private sealed record CatalogNames(
        IReadOnlyDictionary<Guid, string> Items,
        IReadOnlyDictionary<Guid, (string Name, IReadOnlyList<Domain.Catalog.BundleItem> Items)> Bundles);
}
