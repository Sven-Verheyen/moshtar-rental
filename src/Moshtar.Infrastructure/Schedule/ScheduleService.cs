using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Maps;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Schedule;

internal sealed class ScheduleService(AppDbContext db, ITenantContext tenantContext, IMapService maps, ILogger<ScheduleService> logger) : ISchedule
{
    /// <summary>Het back office is Nederlandstalig.</summary>
    private const string Culture = "nl";

    public async Task<ScheduleDay> GetDayAsync(DateOnly day, CancellationToken ct = default)
    {
        var reservations = await db.Reservations.AsNoTracking().Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled && (r.StartDate == day || r.EndDate == day))
            .ToListAsync(ct);
        await FindMissingPointsAsync(reservations, ct);
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
            Returns: Select(DeliveryMethod.Pickup, r => r.EndDate == day),
            BusinessPoint: await BusinessPointAsync(ct));
    }

    /// <summary>
    /// Zoekt de punten op van leveradressen die er nog geen hebben: reservaties van voor er punten waren, of waarvan
    /// de kaartdienst niet antwoordde bij het reserveren. Een adres dat niet gevonden wordt, blijft zonder punt.
    /// </summary>
    private async Task FindMissingPointsAsync(IReadOnlyList<Reservation> reservations, CancellationToken ct)
    {
        var missing = reservations.Where(r => r is { DeliveryMethod: DeliveryMethod.Delivery, DeliveryAddress: not null, DeliveryPoint: null }).ToList();
        var found = new Dictionary<Guid, GeoPoint>();
        foreach (var reservation in missing)
            if (await maps.TryFindAsync(reservation.DeliveryAddress!, logger, ct) is { } point)
                found[reservation.Id] = reservation.DeliveryPoint = point;
        if (found.Count == 0) return;

        var ids = found.Keys.ToList();
        // Enkel waar intussen niemand een punt zette (een speld, een verbeterd adres).
        foreach (var tracked in await db.Reservations.Where(r => ids.Contains(r.Id) && r.DeliveryPoint == null).ToListAsync(ct))
            tracked.DeliveryPoint = found[tracked.Id];
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>Het punt van het bedrijfsadres, opgezocht bij het eerste gebruik (en opnieuw na een adreswijziging).</summary>
    private async Task<GeoPoint?> BusinessPointAsync(CancellationToken ct)
    {
        var id = tenantContext.TenantId;
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null || tenant.AddressPoint is not null || string.IsNullOrWhiteSpace(tenant.Street) || string.IsNullOrWhiteSpace(tenant.PostalCode))
            return tenant?.AddressPoint;
        var address = new Address { Street = tenant.Street, PostalCode = tenant.PostalCode, City = tenant.City ?? "" };
        tenant.AddressPoint = await maps.TryFindAsync(address, logger, ct);
        if (tenant.AddressPoint is not null)
            await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return tenant.AddressPoint;
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
        r.Period,
        r.DeliveryMethod == DeliveryMethod.Delivery ? r.DeliveryPoint : null,
        r.DeliveryPointConfirmed);

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
