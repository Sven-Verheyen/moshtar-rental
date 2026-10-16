using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Images;
using Moshtar.Application.Mail;
using Moshtar.Application.Reviews;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Reviews;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Reviews;

internal sealed class ReviewRequests(
    AppDbContext db, ITenantContext tenantContext, IMailer mailer, IImageStore images, IReviewOptOuts optOuts, IReviewOptOutLinks optOutLinks,
    ILogger<ReviewRequests> logger) : IReviewRequests
{
    public async Task<int> SendDueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
        // Op de microseconde, zoals de databank het bewaart: zo herkent het vrijgeven van een claim zijn eigen tijdstip.
        nowUtc = new DateTime(nowUtc.Ticks - nowUtc.Ticks % 10, DateTimeKind.Utc);
        // Het tijdstip van het aanzetten opnieuw lezen: de verhuurder in de context kan van voor een wijziging zijn.
        var settings = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.Id)
            .Select(t => new { t.ReviewLink, t.ReviewRequestsSince }).SingleAsync(ct);
        if (settings.ReviewRequestsSince is not { } since || BusinessDetails.Link(settings.ReviewLink) is not { } reviewLink)
            return 0;
        if (ReviewRequestRule.EndDaysForMail(new DateTimeOffset(nowUtc), since) is not { } endDays) return 0;

        // Ook wat al gemaild werd: een reservatie die er later bij kwam voor dezelfde klant en einddag, krijgt geen tweede mail.
        var reservations = await db.Reservations.Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled && r.EndDate >= endDays.Start && r.EndDate <= endDays.End)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Number)
            .ToListAsync(ct);
        if (!reservations.Any(r => r.ReviewRequestMailedAtUtc == null)) return 0;

        var host = await db.PrimaryHostAsync(tenant.Id, ct);
        if (host is null)
        {
            logger.LogWarning("Verhuurder {Tenant} heeft geen domein: geen reviewvragen.", tenant.Slug);
            return 0;
        }
        var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => t.Kind == MailKind.ReviewRequest).ToListAsync(ct);

        // Eén mail per klant en einddag, voor al zijn reservaties die dan eindigden; wie zich afmeldde, krijgt niets. Zijn
        // reservaties blijven zonder reviewvraag, zodat ze ook in de Planning niet meer opduiken.
        var groups = ReviewRequestRule.PerCustomer(reservations).ToList();
        var optedOut = await optOuts.OptedOutAmongAsync(groups.Select(g => g.Key.Email), ct);

        var sent = 0;
        foreach (var group in groups.Where(g => !optedOut.Contains(g.Key.Email)))
        {
            var pending = group.Where(r => r.ReviewRequestMailedAtUtc == null).ToList();
            if (pending.Count == 0) continue;
            var ids = pending.Select(r => r.Id).ToList();
            // Eerst opeisen, dan mailen: zo vertrekt er nooit een tweede, ook niet als het versturen twee keer tegelijk
            // loopt. Mislukt de mail, dan wordt de claim vrijgegeven zodat een volgende run het opnieuw probeert.
            var claimed = await db.Reservations
                .Where(r => ids.Contains(r.Id) && r.ReviewRequestMailedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewRequestMailedAtUtc, nowUtc), ct);
            if (claimed == 0) continue;

            if (pending.Count < group.Count())
            {
                // De klant kreeg voor deze einddag al een reviewvraag: alleen de historieklijn, geen tweede mail.
                foreach (var reservation in pending) reservation.RecordReviewRequestMailed(nowUtc);
                await db.SaveChangesAsync(CancellationToken.None);
                continue;
            }

            var first = pending[0];
            var language = ReservationConfirmation.LanguageOf(first, tenant);
            var template = ReviewRequestMail.Template(customizations.SingleOrDefault(c => c.Culture == language), language);
            try
            {
                await mailer.SendAsync(ReviewRequestMail.Create(first, tenant, images.LogoUrl(tenant), template, reviewLink, ReviewRequestMail.QrCodeUrl(host),
                    optOutLinks.For(host, language, first.Customer!.Email)),
                    CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Reviewvraag voor reservatie {Number} van verhuurder {Tenant} kon niet vertrekken.", first.Number, tenant.Slug);
                await db.Reservations.Where(r => ids.Contains(r.Id) && r.ReviewRequestMailedAtUtc == nowUtc)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewRequestMailedAtUtc, (DateTime?)null), CancellationToken.None);
                continue;
            }

            sent++;
            foreach (var reservation in pending) reservation.RecordReviewRequestMailed(nowUtc);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return sent;
    }
}
