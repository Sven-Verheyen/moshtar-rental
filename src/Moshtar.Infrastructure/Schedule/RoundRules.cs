using Microsoft.EntityFrameworkCore;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Schedule;

/// <summary>Wat <see cref="RoundPlanner"/> en <see cref="RoundMessages"/> delen: wie in een ronde hoort, het slot en de meldingen.</summary>
internal static class RoundRules
{
    public const string NoRoute = "Deze ronde heeft nog geen route. Stel eerst een route voor.";
    public const string NotInRound = "Deze reservatie zit niet in de ronde. Werk de ronde eerst bij.";
    public const string Changed = "Werk eerst de ronde bij: er kwam een reservatie bij, er viel er een weg of een adres veranderde sinds de route berekend werd. Zo krijgt elke klant het juiste venster.";

    /// <summary>Een venster over middernacht valt niet op de dag van de ronde, die de mail en het WhatsApp-bericht noemen.</summary>
    public static string PastMidnight(IEnumerable<string> numbers) =>
        $"Het venster van reservatie {string.Join(", ", numbers)} loopt over middernacht: de dag in het bericht zou niet kloppen. Vertrek vroeger of verdeel de stops, of verwittig de klant zelf.";

    /// <summary>
    /// De reservaties die in de ronde horen: leveringen die deze dag beginnen (leverronde) of eindigen (ophaalronde),
    /// niet geannuleerd. Of hun punt nog dat van de stop is, controleert de oproeper.
    /// </summary>
    public static IQueryable<Reservation> InRound(this IQueryable<Reservation> reservations, DateOnly day, RoundKind kind)
    {
        var members = reservations.Where(r => r.Status != ReservationStatus.Cancelled && r.DeliveryMethod == DeliveryMethod.Delivery);
        return kind == RoundKind.Delivery ? members.Where(r => r.StartDate == day) : members.Where(r => r.EndDate == day);
    }

    /// <summary>
    /// Weigert met <see cref="Changed"/> als de bewaarde ronde niet meer klopt (<see cref="Round.IsCurrent"/>): een
    /// scherm dat al openstond, kan anders vensters van een verouderde ronde versturen.
    /// </summary>
    public static async Task EnsureCurrentAsync(AppDbContext db, Round round, CancellationToken ct)
    {
        var members = await db.Reservations.AsNoTracking().InRound(round.Day, round.Kind)
            .Where(r => r.DeliveryPoint != null)
            .Select(r => new { r.Id, r.DeliveryPoint })
            .ToListAsync(ct);
        var businessPoint = await db.Tenants.AsNoTracking().Where(t => t.Id == round.TenantId).Select(t => t.AddressPoint).SingleOrDefaultAsync(ct);
        if (!round.IsCurrent(members.Select(m => (m.Id, m.DeliveryPoint!)), businessPoint))
            throw new InvalidOperationException(Changed);
    }

    /// <summary>
    /// Wijzigingen aan de rondes van een tenant na elkaar, tot het einde van de transactie: twee keer tegelijk "Route
    /// voorstellen" maakt zo geen twee rondes, en een mail vertrekt niet twee keer. Een eigen sleutel, zodat reservaties
    /// niet wachten op de kaartdienst.
    /// </summary>
    public static Task LockAsync(AppDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ronde:" + tenantId}, 0))", ct);
}
