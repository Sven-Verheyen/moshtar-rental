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
    /// Wijzigingen aan de rondes van een tenant na elkaar, tot het einde van de transactie: twee keer tegelijk "Route
    /// voorstellen" maakt zo geen twee rondes, en een mail vertrekt niet twee keer. Een eigen sleutel, zodat reservaties
    /// niet wachten op de kaartdienst.
    /// </summary>
    public static Task LockAsync(AppDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ronde:" + tenantId}, 0))", ct);
}
