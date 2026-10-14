using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Mail;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Schedule;

internal sealed class RoundMessages(AppDbContext db, ITenantContext tenantContext, TimeProvider clock, IMailer mailer, ILogger<RoundMessages> logger)
    : IRoundMessages
{
    public async Task<int> MailExpectedTimesAsync(DateOnly day, RoundKind kind, ReservationActor actor, CancellationToken ct = default)
    {
        var tenantId = tenantContext.TenantId;
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
        var round = await db.Rounds.SingleOrDefaultAsync(r => r.Day == day && r.Kind == kind, ct)
            ?? throw new InvalidOperationException("Deze ronde heeft nog geen route. Stel eerst een route voor.");

        // Wie zijn huidige venster al kreeg, krijgt niets; een geannuleerde reservatie evenmin.
        var due = round.Ordered.Where(s => s.MailedAt is null || s.MailedWindowStart != s.WindowStart || s.MailedWindowEnd != s.WindowEnd).ToList();
        var ids = due.Select(s => s.ReservationId).ToList();
        var reservations = await db.Reservations.Include(r => r.Customer)
            .Where(r => ids.Contains(r.Id) && r.Status != ReservationStatus.Cancelled)
            .ToDictionaryAsync(r => r.Id, ct);
        var mailKind = ExpectedTimeMail.KindFor(kind);
        var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => t.Kind == mailKind).ToListAsync(ct);

        var mailed = 0;
        foreach (var stop in due)
        {
            if (!reservations.TryGetValue(stop.ReservationId, out var reservation)) continue;
            var language = ReservationConfirmation.LanguageOf(reservation, tenant);
            var template = customizations.SingleOrDefault(c => c.Culture == language)?.Text ?? MailKinds.For(mailKind).Standard(language);
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
        return mailed;
    }
}
