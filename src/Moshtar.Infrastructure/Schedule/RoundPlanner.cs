using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Maps;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Schedule;

internal sealed class RoundPlanner(AppDbContext db, ITenantContext tenantContext, IMapService maps, ILogger<RoundPlanner> logger) : IRoundPlanner
{
    private static readonly TimeZoneInfo BelgianTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public async Task ProposeAsync(DateOnly day, RoundKind kind, CancellationToken ct = default)
    {
        var tenant = await TenantAsync(ct);
        var start = await BusinessPointAsync(tenant, ct);
        var candidates = await CandidatesAsync(day, kind, ct);
        if (candidates.Count == 0)
            throw new InvalidOperationException("Er is geen stop met een adres op de kaart om een route mee te maken.");

        var round = await FindAsync(day, kind, ct);
        if (round is null)
        {
            round = new Round { Day = day, Kind = kind, Departure = tenant.RoundSettings.Departure(kind) };
            db.Rounds.Add(round);
        }
        var route = await MapsAsync(() => maps.RouteAsync(start, candidates.Select(c => c.Point).ToList(), Departure(day, round.Departure), bestOrder: true, ct));

        // Wat de gebruiker per stop koos (vast uur, eigen tijd) en wat de klant al kreeg, blijft bij de stop.
        foreach (var gone in round.Stops.Where(s => candidates.All(c => c.ReservationId != s.ReservationId)).ToList())
            round.Stops.Remove(gone);
        for (var i = 0; i < route.Order.Count; i++)
        {
            var candidate = candidates[route.Order[i]];
            var stop = round.Stops.SingleOrDefault(s => s.ReservationId == candidate.ReservationId);
            if (stop is null)
            {
                stop = new RoundStop { ReservationId = candidate.ReservationId };
                round.Stops.Add(stop);
            }
            stop.Position = i + 1;
            stop.Point = candidate.Point;
        }
        ApplyLegs(round, route.Legs);
        await SaveAsync(round, tenant, ct);
    }

    public Task MoveAsync(DateOnly day, RoundKind kind, Guid reservationId, int position, CancellationToken ct = default) =>
        ChangeAsync(day, kind, ct, async (round, tenant) =>
        {
            var ordered = round.Ordered.ToList();
            var stop = Stop(round, reservationId);
            ordered.Remove(stop);
            ordered.Insert(Math.Clamp(position, 1, ordered.Count + 1) - 1, stop);
            Renumber(ordered);
            await RefreshLegsAsync(round, tenant, ct);
        });

    public Task SetFixedTimeAsync(DateOnly day, RoundKind kind, Guid reservationId, TimeOnly? time, CancellationToken ct = default) =>
        ChangeAsync(day, kind, ct, (round, _) =>
        {
            Stop(round, reservationId).FixedTime = time;
            return Task.CompletedTask;
        });

    public Task SetHandlingMinutesAsync(DateOnly day, RoundKind kind, Guid reservationId, int? minutes, CancellationToken ct = default) =>
        ChangeAsync(day, kind, ct, (round, _) =>
        {
            if (minutes is { } m && !RoundSettings.IsHandlingTime(m))
                throw new InvalidOperationException("De opbouw- of afbouwtijd moet tussen 0 en 480 minuten liggen.");
            Stop(round, reservationId).HandlingMinutes = minutes;
            return Task.CompletedTask;
        });

    /// <summary>Een ander vertrekuur is ander verkeer: de reistijden worden opnieuw gevraagd.</summary>
    public Task SetDepartureAsync(DateOnly day, RoundKind kind, TimeOnly departure, CancellationToken ct = default) =>
        ChangeAsync(day, kind, ct, async (round, tenant) =>
        {
            round.Departure = departure;
            await RefreshLegsAsync(round, tenant, ct);
        });

    public Task UpdateAsync(DateOnly day, RoundKind kind, CancellationToken ct = default) =>
        ChangeAsync(day, kind, ct, async (round, tenant) =>
        {
            var start = await BusinessPointAsync(tenant, ct);
            var candidates = await CandidatesAsync(day, kind, ct);
            // Weggevallen, of met een ander punt (een verbeterd adres): eruit, en wie er nog bij hoort komt er opnieuw in.
            foreach (var gone in round.Stops.Where(s => !candidates.Any(c => c.ReservationId == s.ReservationId && c.Point == s.Point)).ToList())
                round.Stops.Remove(gone);
            var added = candidates.Where(c => round.Stops.All(s => s.ReservationId != c.ReservationId)).ToList();
            await RefreshLegsAsync(round, tenant, ct);

            var departure = Departure(day, round.Departure);
            foreach (var candidate in added)
            {
                var ordered = round.Ordered.ToList();
                var points = ordered.Select(s => s.Point).ToList();
                var legs = ordered.Select(s => TimeSpan.FromMinutes(s.TravelMinutes)).Append(TimeSpan.FromMinutes(round.ReturnTravelMinutes)).ToList();
                var toNew = await MapsAsync(() => maps.TravelTimesAsync(points.Prepend(start).ToList(), [candidate.Point], departure, ct));
                var fromNew = await MapsAsync(() => maps.TravelTimesAsync([candidate.Point], points.Append(start).ToList(), departure, ct));
                var position = RoundInsertion.CheapestPosition(legs, toNew.Select(row => row[0]).ToList(), fromNew[0]);

                var stop = new RoundStop { ReservationId = candidate.ReservationId, Point = candidate.Point };
                round.Stops.Add(stop);
                Renumber(RoundInsertion.Insert(ordered, stop, position));
                // De reistijden rond de nieuwe stop, voor de volgende nieuwe stop; daarna vraagt de route ze allemaal opnieuw.
                stop.TravelMinutes = Minutes(toNew[position][0]);
                if (position < ordered.Count) ordered[position].TravelMinutes = Minutes(fromNew[0][position]);
                else round.ReturnTravelMinutes = Minutes(fromNew[0][position]);
            }
            if (added.Count > 0) await RefreshLegsAsync(round, tenant, ct);
        });

    /// <summary>Laadt de ronde, past ze aan, rekent de uren opnieuw uit en bewaart ze.</summary>
    private async Task ChangeAsync(DateOnly day, RoundKind kind, CancellationToken ct, Func<Round, Tenant, Task> change)
    {
        var tenant = await TenantAsync(ct);
        var round = await FindAsync(day, kind, ct)
            ?? throw new InvalidOperationException("Deze ronde heeft nog geen route. Stel eerst een route voor.");
        await change(round, tenant);
        await SaveAsync(round, tenant, ct);
    }

    private async Task SaveAsync(Round round, Tenant tenant, CancellationToken ct)
    {
        var settings = tenant.RoundSettings;
        round.Recalculate(settings.HandlingMinutes(round.Kind), settings.WindowMinutes);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Vraagt de reistijden van de ronde in haar huidige volgorde opnieuw op, met het verkeer op het vertrekuur.</summary>
    private async Task RefreshLegsAsync(Round round, Tenant tenant, CancellationToken ct)
    {
        var start = await BusinessPointAsync(tenant, ct);
        var ordered = round.Ordered;
        var route = await MapsAsync(() => maps.RouteAsync(start, ordered.Select(s => s.Point).ToList(), Departure(round.Day, round.Departure), bestOrder: false, ct));
        ApplyLegs(round, route.Legs);
    }

    private static void ApplyLegs(Round round, IReadOnlyList<TimeSpan> legs)
    {
        var ordered = round.Ordered;
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].TravelMinutes = Minutes(legs[i]);
        round.ReturnTravelMinutes = Minutes(legs[ordered.Count]);
    }

    private static void Renumber(IReadOnlyList<RoundStop> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Position = i + 1;
    }

    private static int Minutes(TimeSpan travel) => (int)Math.Round(travel.TotalMinutes, MidpointRounding.AwayFromZero);

    private static RoundStop Stop(Round round, Guid reservationId) =>
        round.Stops.SingleOrDefault(s => s.ReservationId == reservationId)
            ?? throw new InvalidOperationException("Deze reservatie zit niet in de ronde. Werk de ronde eerst bij.");

    /// <summary>Het vertrek op die dag, in Belgische tijd: daarop rekent de kaartdienst het verwachte verkeer.</summary>
    private static DateTimeOffset Departure(DateOnly day, TimeOnly departure)
    {
        var local = day.ToDateTime(departure);
        return new DateTimeOffset(local, BelgianTime.GetUtcOffset(local));
    }

    private Task<Round?> FindAsync(DateOnly day, RoundKind kind, CancellationToken ct) =>
        db.Rounds.SingleOrDefaultAsync(r => r.Day == day && r.Kind == kind, ct);

    /// <summary>
    /// De reservaties die in de ronde horen: leveringen die deze dag beginnen (leverronde) of eindigen (ophaalronde),
    /// niet geannuleerd, met een punt op de kaart. Op postcode, zodat nieuwe stops altijd in dezelfde volgorde komen.
    /// </summary>
    private async Task<IReadOnlyList<(Guid ReservationId, GeoPoint Point)>> CandidatesAsync(DateOnly day, RoundKind kind, CancellationToken ct)
    {
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.Status != ReservationStatus.Cancelled && r.DeliveryMethod == DeliveryMethod.Delivery
                && (kind == RoundKind.Delivery ? r.StartDate == day : r.EndDate == day))
            .ToListAsync(ct);
        return reservations
            .Where(r => r.DeliveryPoint is not null)
            .OrderBy(r => r.DeliveryAddress?.PostalCode).ThenBy(r => r.DeliveryAddress?.Street).ThenBy(r => r.Number)
            .Select(r => (r.Id, r.DeliveryPoint!))
            .ToList();
    }

    private async Task<Tenant> TenantAsync(CancellationToken ct)
    {
        var id = tenantContext.TenantId;
        return await db.Tenants.SingleAsync(t => t.Id == id, ct);
    }

    /// <summary>Het punt van het bedrijfsadres, waar elke ronde vertrekt en eindigt; opgezocht als het er nog niet is.</summary>
    private async Task<GeoPoint> BusinessPointAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.AddressPoint is null && !string.IsNullOrWhiteSpace(tenant.Street) && !string.IsNullOrWhiteSpace(tenant.PostalCode))
            tenant.AddressPoint = await maps.TryFindAsync(new Address { Street = tenant.Street, PostalCode = tenant.PostalCode, City = tenant.City ?? "" }, logger, ct);
        return tenant.AddressPoint
            ?? throw new InvalidOperationException("Het bedrijfsadres staat niet op de kaart. Vul het in bij Bedrijfsgegevens; daar vertrekt elke ronde.");
    }

    /// <summary>Een kaartdienst die niet antwoordt, wordt een melding voor de gebruiker; de ronde blijft zoals ze was.</summary>
    private async Task<T> MapsAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (e is not OperationCanceledException and not InvalidOperationException)
        {
            logger.LogWarning(e, "De kaartdienst kon geen route berekenen.");
            throw new InvalidOperationException("De route kon niet berekend worden: de kaartdienst antwoordt niet. Probeer het later opnieuw.");
        }
    }
}
