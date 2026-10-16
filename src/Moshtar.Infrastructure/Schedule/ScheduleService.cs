using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Maps;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Schedule;

/// <summary>
/// Met een eigen, kortlevende context per oproep: de Planning laadt de dag opnieuw na elke knop, ook als een andere
/// knop in hetzelfde scherm nog bezig is.
/// </summary>
internal sealed class ScheduleService(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, IMapService maps, ILogger<ScheduleService> logger) : ISchedule
{
    /// <summary>Het back office is Nederlandstalig.</summary>
    private const string Culture = "nl";

    /// <summary>
    /// Eén dag openen zoekt ook de ontbrekende punten op (van de leveradressen en het bedrijfsadres) voor de kaart.
    /// De week en de maand niet: die tonen geen kaart, en zo vraagt een maand niet tientallen adressen tegelijk op.
    /// </summary>
    public async Task<ScheduleDay> GetDayAsync(DateOnly day, CancellationToken ct = default) =>
        (await LoadAsync(day, day, findPoints: true, ct))[0];

    public Task<IReadOnlyList<ScheduleDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        LoadAsync(from, to, findPoints: false, ct);

    private async Task<IReadOnlyList<ScheduleDay>> LoadAsync(DateOnly from, DateOnly to, bool findPoints, CancellationToken ct)
    {
        if (to < from) throw new ArgumentException("Het einde ligt voor het begin.", nameof(to));
        await using var db = await contexts.CreateDbContextAsync(ct);
        var reservations = await db.Reservations.AsNoTracking().Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled
                && ((r.StartDate >= from && r.StartDate <= to) || (r.EndDate >= from && r.EndDate <= to)))
            .ToListAsync(ct);
        GeoPoint? businessPoint = null;
        if (findPoints)
        {
            await FindMissingPointsAsync(db, reservations, ct);
            businessPoint = await BusinessPointAsync(db, ct);
        }
        var names = await NamesAsync(db, reservations, ct);
        var rounds = await db.Rounds.AsNoTracking().Where(r => r.Day >= from && r.Day <= to).ToListAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);
        var settings = tenant?.RoundSettings ?? RoundSettings.Default;
        var whatsApp = await WhatsAppLinksAsync(db, tenant, rounds, ct);

        var stops = reservations.Select(r => (Reservation: r, Stop: ToStop(r, names))).ToList();
        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => Day(from.AddDays(i), stops, rounds, settings, businessPoint, tenant?.AddressPoint, whatsApp))
            .ToList();
    }

    private static ScheduleDay Day(
        DateOnly day, IReadOnlyList<(Reservation Reservation, ScheduleStop Stop)> stops, IReadOnlyList<Round> rounds,
        RoundSettings settings, GeoPoint? businessPoint, GeoPoint? currentBusinessPoint, WhatsAppLinks whatsApp)
    {
        var byId = stops.ToDictionary(s => s.Stop.ReservationId, s => s.Reservation);
        IReadOnlyList<ScheduleStop> Select(DeliveryMethod method, Func<Reservation, bool> onDay) => stops
            .Where(s => s.Reservation.DeliveryMethod == method && onDay(s.Reservation))
            .Select(s => s.Stop)
            .OrderBy(s => s.Address?.PostalCode).ThenBy(s => s.Address?.Street).ThenBy(s => s.Number)
            .ToList();

        var deliveries = Select(DeliveryMethod.Delivery, r => r.StartDate == day);
        var collections = Select(DeliveryMethod.Delivery, r => r.EndDate == day);
        var (deliveryRound, orderedDeliveries) = InRound(rounds.SingleOrDefault(r => r.Day == day && r.Kind == RoundKind.Delivery), deliveries, settings, currentBusinessPoint, (round, stop) => whatsApp(byId[stop.ReservationId], round, stop));
        var (collectionRound, orderedCollections) = InRound(rounds.SingleOrDefault(r => r.Day == day && r.Kind == RoundKind.Collection), collections, settings, currentBusinessPoint, (round, stop) => whatsApp(byId[stop.ReservationId], round, stop));
        return new ScheduleDay(
            day,
            Deliveries: orderedDeliveries,
            Collections: orderedCollections,
            Pickups: Select(DeliveryMethod.Pickup, r => r.StartDate == day),
            Returns: Select(DeliveryMethod.Pickup, r => r.EndDate == day),
            BusinessPoint: businessPoint,
            DeliveryRound: deliveryRound,
            CollectionRound: collectionRound);
    }

    /// <summary>
    /// De stops van een ronde in de volgorde van de bewaarde ronde, met hun uren; wat er niet in zit (nieuw, of zonder
    /// punt) erachter op postcode. Zonder bewaarde ronde: de stops zoals ze zijn, op postcode.
    /// </summary>
    private static (ScheduleRound? Round, IReadOnlyList<ScheduleStop> Stops) InRound(
        Round? round, IReadOnlyList<ScheduleStop> stops, RoundSettings settings, GeoPoint? businessPoint, Func<Round, RoundStop, string?> whatsAppLink)
    {
        if (round is null) return (null, stops);
        var saved = round.Stops.ToDictionary(s => s.ReservationId);
        var changed = !round.IsCurrent(stops.Where(s => s.Point is not null).Select(s => (s.ReservationId, s.Point!)), businessPoint);
        // Een gewijzigde ronde heeft vensters die niet meer kloppen: geen WhatsApp-bericht tot ze bijgewerkt is.
        var inRound = stops
            .Where(s => saved.TryGetValue(s.ReservationId, out var stop) && stop.Point == s.Point)
            .Select(s => s with { InRound = ToRoundStop(saved[s.ReservationId], changed ? null : whatsAppLink(round, saved[s.ReservationId])) })
            .OrderBy(s => s.InRound!.Position)
            .ToList();
        var rest = stops.Where(s => inRound.All(r => r.ReservationId != s.ReservationId)).ToList();
        return (new ScheduleRound(round.Departure, round.Return, changed, settings.HandlingMinutes(round.Kind), round.PastMidnight), [.. inRound, .. rest]);
    }

    private static ScheduleRoundStop ToRoundStop(RoundStop s, string? whatsAppLink) =>
        new(s.Position, s.Arrival, s.Window, s.FixedTime, s.HandlingMinutes, s.Late,
            s is { MailedWindowStart: { } start, MailedWindowEnd: { } end } ? new ExpectedWindow(start, end) : null, s.MailedAt,
            whatsAppLink, s.WhatsAppAt, s.WindowPastMidnight);

    /// <summary>De WhatsApp-link van een stop in een ronde, of null zonder geldig telefoonnummer.</summary>
    private delegate string? WhatsAppLinks(Reservation reservation, Round round, RoundStop stop);

    /// <summary>
    /// Maakt de WhatsApp-links met het bericht uit het sjabloon Verwacht leveruur of Verwacht ophaaluur, in de taal van
    /// elke reservatie. Zonder rondes worden de sjablonen niet opgehaald.
    /// </summary>
    private static async Task<WhatsAppLinks> WhatsAppLinksAsync(AppDbContext db, Tenant? tenant, IReadOnlyList<Round> rounds, CancellationToken ct)
    {
        if (tenant is null || rounds.Count == 0) return (_, _, _) => null;
        MailKind[] kinds = [MailKind.ExpectedDeliveryTime, MailKind.ExpectedCollectionTime];
        var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => kinds.Contains(t.Kind)).ToListAsync(ct);
        return (reservation, round, stop) =>
        {
            // Het bericht noemt de dag van de ronde; na middernacht klopt die niet.
            if (stop.WindowPastMidnight) return null;
            var template = ExpectedTimeMail.Template(customizations, ExpectedTimeMail.KindFor(round.Kind), ReservationConfirmation.LanguageOf(reservation, tenant));
            return ExpectedTimeMail.WhatsAppLink(reservation, tenant, template, round.Day, stop.Window);
        };
    }

    /// <summary>
    /// Zoekt de punten op van leveradressen die er nog geen hebben: reservaties van voor er punten waren, of waarvan
    /// de kaartdienst niet antwoordde bij het reserveren. Een adres dat niet gevonden wordt, blijft zonder punt.
    /// </summary>
    private async Task FindMissingPointsAsync(AppDbContext db, IReadOnlyList<Reservation> reservations, CancellationToken ct)
    {
        var missing = reservations.Where(r => r is { DeliveryMethod: DeliveryMethod.Delivery, DeliveryAddress: not null, DeliveryPoint: null }).ToList();
        foreach (var reservation in missing)
        {
            var address = reservation.DeliveryAddress!;
            if (await maps.TryFindAsync(address, logger, ct) is not { } point) continue;
            // Enkel als het adres intussen niet verbeterd werd en niemand een punt of speld zette: anders kwam het punt
            // van het oude adres bij het nieuwe, of verdween een bevestigde speld.
            var saved = await db.Reservations
                .Where(r => r.Id == reservation.Id && r.DeliveryPoint == null && !r.DeliveryPointConfirmed
                    && r.DeliveryAddress!.Street == address.Street && r.DeliveryAddress.PostalCode == address.PostalCode
                    && r.DeliveryAddress.City == address.City && r.DeliveryAddress.Country == address.Country)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.DeliveryPoint!.Latitude, point.Latitude)
                    .SetProperty(r => r.DeliveryPoint!.Longitude, point.Longitude), ct);
            if (saved == 1) reservation.DeliveryPoint = point;
        }
    }

    /// <summary>Het punt van het bedrijfsadres, opgezocht bij het eerste gebruik (en opnieuw na een adreswijziging).</summary>
    private Task<GeoPoint?> BusinessPointAsync(AppDbContext db, CancellationToken ct) =>
        maps.FindBusinessPointAsync(db, tenantContext.TenantId, logger, ct);

    private static ScheduleStop ToStop(Reservation r, CatalogNames names) => new(
        r.Id,
        r.Number,
        $"{r.Customer?.FirstName} {r.Customer?.LastName}".Trim(),
        string.IsNullOrWhiteSpace(r.Customer?.Phone) ? null : r.Customer.Phone,
        r.DeliveryMethod == DeliveryMethod.Delivery ? r.DeliveryAddress : null,
        // Een meerprijs of korting is niets om mee te nemen.
        r.Lines.Where(l => !l.IsPriceOnly).Select(l => ToLine(l, names)).ToList(),
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
    private async Task<CatalogNames> NamesAsync(AppDbContext db, IReadOnlyList<Reservation> reservations, CancellationToken ct)
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
