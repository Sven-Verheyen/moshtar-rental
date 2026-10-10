using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reservations;

internal sealed class ReservationService(
    AppDbContext db, ITenantContext tenantContext, TimeProvider clock, IMailer mailer, ILogger<ReservationService> logger) : IReservationService
{
    private static readonly ReservationStatus[] OccupyingStatuses =
        [ReservationStatus.Confirmed, ReservationStatus.Delivered, ReservationStatus.Returned];

    public async Task<IReadOnlyList<StockShortage>> CheckAvailabilityAsync(
        DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct = default)
    {
        var (shortages, _, _) = await EvaluateAsync(period, lines, ct);
        return shortages;
    }

    public async Task<ReservationResult> ReserveAsync(ReservationRequest request, ReservationActor actor, CancellationToken ct = default)
    {
        Validate(request);
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Reservaties per tenant na elkaar afhandelen, tot het einde van de transactie.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenant.Id.ToString()}, 0))", ct);

        var (shortages, items, bundles) = await EvaluateAsync(request.Period, request.Lines, ct);
        if (shortages.Count > 0)
            return new ReservationResult(null, shortages);

        var customer = await UpsertCustomerAsync(request, ct);
        var days = request.Period.Days;
        var reservation = new Reservation
        {
            Number = await NextNumberAsync(ct),
            Customer = customer,
            StartDate = request.Period.Start,
            EndDate = request.Period.End,
            Status = ReservationStatus.Confirmed,
            DeliveryMethod = request.DeliveryMethod,
            DeliveryAddress = request.DeliveryMethod == DeliveryMethod.Delivery ? request.DeliveryAddress ?? request.Customer.Address : null,
            Notes = request.Notes,
            Culture = request.Culture,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Lines = request.Lines.Select(l => CreateLine(l, items, bundles, days, request.Culture, tenant.DefaultCulture)).ToList(),
        };
        reservation.TotalPrice = reservation.Lines.Sum(l => l.LineTotal);
        reservation.RecordCreated(actor, reservation.CreatedAtUtc);

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await SendConfirmationAsync(reservation, tenant, ct);
        return new ReservationResult(reservation, []);
    }

    /// <summary>
    /// Bevestigt de reservatie per mail aan de klant. Pas na de commit, en een mislukte mail maakt de reservatie
    /// niet ongedaan: die staat vast, de verhuurder kan de klant nog altijd zelf contacteren.
    /// </summary>
    private async Task SendConfirmationAsync(Reservation reservation, Tenant tenant, CancellationToken ct)
    {
        try
        {
            await mailer.SendAsync(ReservationConfirmation.Create(reservation, tenant), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Bevestigingsmail voor reservatie {Number} van verhuurder {Tenant} kon niet vertrekken.", reservation.Number, tenant.Slug);
        }
    }

    public async Task<Reservation> ChangeStatusAsync(Guid reservationId, ReservationStatus status, ReservationActor actor, CancellationToken ct = default)
    {
        // In het back office leeft deze service zo lang als het scherm open staat: altijd vers inlezen,
        // zodat een wijziging door een collega intussen niet over het hoofd gezien wordt.
        db.ChangeTracker.Clear();
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new InvalidOperationException("Reservatie niet gevonden.");
        reservation.ChangeStatus(status, actor, clock.GetUtcNow().UtcDateTime);
        // De nieuwe status en de gebeurtenis in de historiek gaan samen in één SaveChanges, dus één transactie.
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<IReadOnlyList<ReservationHistoryEntry>> GetHistoryAsync(Guid reservationId, CancellationToken ct = default) =>
        await (from e in db.ReservationEvents.AsNoTracking()
               where e.ReservationId == reservationId
               // Ook uitgeschakelde gebruikers blijven met hun e-mailadres zichtbaar.
               join u in db.Users on e.UserId equals u.Id into users
               from u in users.DefaultIfEmpty()
               orderby e.OccurredAtUtc descending, e.Kind descending
               select new ReservationHistoryEntry(e.OccurredAtUtc, e.Kind, e.Status, u.Email))
            .ToListAsync(ct);

    private async Task<(IReadOnlyList<StockShortage> Shortages, Dictionary<Guid, RentalItem> Items, Dictionary<Guid, Bundle> Bundles)> EvaluateAsync(
        DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");
        var before = tenant.BufferDaysBefore;
        var after = tenant.BufferDaysAfter;

        // Bestaande reservaties waarvan de periode (met buffer) de gevraagde periode (met buffer) kan raken.
        var windowStart = period.Start.AddDays(-(before + after));
        var windowEnd = period.End.AddDays(before + after);
        var existing = await db.Reservations.AsNoTracking()
            .Where(r => OccupyingStatuses.Contains(r.Status) && r.StartDate <= windowEnd && r.EndDate >= windowStart)
            .ToListAsync(ct);
        var blockouts = await db.Blockouts.AsNoTracking()
            .Where(b => b.StartDate <= period.End.AddDays(after) && b.EndDate >= period.Start.AddDays(-before))
            .ToListAsync(ct);

        var bundleIds = lines.Select(l => l.BundleId)
            .Concat(existing.SelectMany(r => r.Lines).Select(l => l.BundleId))
            .OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var requestLines = lines.Select(l => new ReservationLine { RentalItemId = l.RentalItemId, BundleId = l.BundleId, Quantity = l.Quantity }).ToList();
        if (requestLines.Any(l => l.BundleId is { } id && (!bundles.TryGetValue(id, out var b) || !b.IsActive)))
            throw new ArgumentException("Onbekend of inactief pakket.");
        var requested = DemandExpander.Expand(requestLines, period, bundles).ToList();

        var itemIds = requested.Select(d => d.RentalItemId).Distinct().ToList();
        var items = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        if (lines.Any(l => l.RentalItemId is { } id && (!items.TryGetValue(id, out var i) || !i.IsActive)))
            throw new ArgumentException("Onbekend of inactief artikel.");

        var reserved = existing.SelectMany(r => DemandExpander.Expand(r.Lines, r.Period, bundles));
        var blocked = blockouts.Select(b => new StockDemand(b.RentalItemId, b.Quantity, new DateRange(b.StartDate, b.EndDate)));
        var stock = items.ToDictionary(kv => kv.Key, kv => kv.Value.Stock);

        var shortages = AvailabilityCalculator.FindShortages(stock, reserved, blocked, requested, before, after);
        return (shortages, items, bundles);
    }

    private static ReservationLine CreateLine(ReservationLineRequest l, Dictionary<Guid, RentalItem> items, Dictionary<Guid, Bundle> bundles,
        int days, string culture, string fallbackCulture)
    {
        if (l.RentalItemId is { } itemId)
        {
            var item = items[itemId];
            return new ReservationLine
            {
                RentalItemId = itemId,
                Quantity = l.Quantity,
                Description = item.Translations.For(culture, fallbackCulture)?.Name ?? item.Slug,
                UnitPrice = item.Pricing.PriceFor(days),
            };
        }
        var bundle = bundles[l.BundleId!.Value];
        return new ReservationLine
        {
            BundleId = bundle.Id,
            Quantity = l.Quantity,
            Description = bundle.Translations.For(culture, fallbackCulture)?.Name ?? bundle.Slug,
            UnitPrice = bundle.Pricing.PriceFor(days),
        };
    }

    private async Task<Customer> UpsertCustomerAsync(ReservationRequest request, CancellationToken ct)
    {
        var email = request.Customer.Email.Trim().ToLowerInvariant();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer is null)
        {
            customer = new Customer { Email = email };
            db.Customers.Add(customer);
        }
        customer.FirstName = request.Customer.FirstName;
        customer.LastName = request.Customer.LastName;
        customer.Phone = request.Customer.Phone;
        customer.Address = request.Customer.Address;
        customer.PreferredCulture = request.Culture;
        return customer;
    }

    /// <summary>Volgnummer per jaar, bv. 2026-0042. Veilig omdat we binnen de tenant-lock zitten.</summary>
    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var year = clock.GetUtcNow().Year;
        var prefix = $"{year}-";
        var count = await db.Reservations.CountAsync(r => r.Number.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:0000}";
    }

    private void Validate(ReservationRequest request)
    {
        if (request.Lines.Count == 0)
            throw new ArgumentException("Een reservatie heeft minstens één artikel of pakket nodig.");
        if (request.Lines.Any(l => l.Quantity < 1 || (l.RentalItemId is null) == (l.BundleId is null)))
            throw new ArgumentException("Elke lijn moet precies één artikel of pakket bevatten met aantal ≥ 1.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (request.Period.Start < today)
            throw new ArgumentException("De huurperiode mag niet in het verleden liggen.");
        if (string.IsNullOrWhiteSpace(request.Customer.Email))
            throw new ArgumentException("E-mailadres is verplicht.");
    }
}
