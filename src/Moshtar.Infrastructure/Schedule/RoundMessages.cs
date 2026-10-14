using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Mail;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Schedule;

/// <summary>Met een eigen, kortlevende context per oproep, zoals <see cref="RoundPlanner"/>.</summary>
internal sealed class RoundMessages(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock, IMailer mailer, ILogger<RoundMessages> logger)
    : IRoundMessages
{
    public async Task<int> MailExpectedTimesAsync(DateOnly day, RoundKind kind, ReservationActor actor, CancellationToken ct = default)
    {
        var tenantId = tenantContext.TenantId;
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RoundRules.LockAsync(db, tenantId, ct);
        var mailed = 0;
        try
        {
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
            var round = await db.Rounds.SingleOrDefaultAsync(r => r.Day == day && r.Kind == kind, ct)
                ?? throw new InvalidOperationException(RoundRules.NoRoute);

            // Wie zijn huidige venster al kreeg, krijgt niets.
            var due = round.Ordered.Where(s => s.MailedAt is null || s.MailedWindowStart != s.WindowStart || s.MailedWindowEnd != s.WindowEnd).ToList();
            var ids = due.Select(s => s.ReservationId).ToList();
            // Enkel wie nog in de ronde hoort, met het punt waarmee zijn venster berekend werd: geen mail voor een
            // geannuleerde of verzette reservatie, of een verbeterd adres waarvan het venster nog niet herrekend is.
            var reservations = await db.Reservations.Include(r => r.Customer).InRound(day, kind)
                .Where(r => ids.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);
            var mailKind = ExpectedTimeMail.KindFor(kind);
            var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => t.Kind == mailKind).ToListAsync(ct);

            foreach (var stop in due)
            {
                if (!reservations.TryGetValue(stop.ReservationId, out var reservation) || reservation.DeliveryPoint != stop.Point) continue;
                var language = ReservationConfirmation.LanguageOf(reservation, tenant);
                var template = ExpectedTimeMail.Template(customizations, mailKind, language);
                try
                {
                    await mailer.SendAsync(ExpectedTimeMail.Create(reservation, tenant, template, day, stop.Window), ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Mail met het verwacht tijdsvenster voor reservatie {Number} kon niet vertrekken.", reservation.Number);
                    throw new InvalidOperationException(
                        $"De mail voor reservatie {reservation.Number} kon niet vertrekken; {mailed} klant(en) kregen hun mail al. Probeer later opnieuw: wie al gemaild werd, krijgt geen tweede.");
                }

                // Na elke mail bewaren: mislukt een volgende mail, dan krijgt wie al gemaild werd geen tweede bij een nieuwe poging.
                var now = clock.GetUtcNow();
                (stop.MailedWindowStart, stop.MailedWindowEnd, stop.MailedAt) = (stop.WindowStart, stop.WindowEnd, now);
                reservation.RecordExpectedTimeMailed(actor, now.UtcDateTime);
                await db.SaveChangesAsync(ct);
                mailed++;
            }
        }
        catch when (mailed > 0)
        {
            // Wat al gemaild en bewaard werd, blijft bewaard, ook als een volgende mail mislukt.
            await CommitQuietlyAsync(tx);
            throw;
        }
        await tx.CommitAsync(ct);
        return mailed;
    }

    public async Task RecordWhatsAppAsync(DateOnly day, RoundKind kind, Guid reservationId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RoundRules.LockAsync(db, tenantContext.TenantId, ct);
        var round = await db.Rounds.SingleOrDefaultAsync(r => r.Day == day && r.Kind == kind, ct)
            ?? throw new InvalidOperationException(RoundRules.NoRoute);
        var stop = round.Stops.SingleOrDefault(s => s.ReservationId == reservationId)
            ?? throw new InvalidOperationException(RoundRules.NotInRound);
        var point = await db.Reservations.AsNoTracking().InRound(day, kind).Where(r => r.Id == reservationId).Select(r => r.DeliveryPoint).ToListAsync(ct);
        if (point is not [{ } current] || current != stop.Point)
            throw new InvalidOperationException(RoundRules.NotInRound);
        stop.WhatsAppAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task CommitQuietlyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        try
        {
            await tx.CommitAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "De al verstuurde mails met het verwacht tijdsvenster konden niet bewaard worden.");
        }
    }
}
