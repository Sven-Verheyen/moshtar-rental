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
    /// <summary>Het uur waarop de reviewvraag de dag na de einddag vertrekt, Belgische tijd.</summary>
    private static readonly TimeOnly SendFrom = new(10, 0);

    /// <summary>Hoeveel dagen na de einddag een reviewvraag nog mag vertrekken; later is te laat.</summary>
    private const int LastDayAfterEnd = 3;

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

        var now = Belgium.Local(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)));
        var today = DateOnly.FromDateTime(now.DateTime);
        // Vandaag om 10u is de beurt aan wie gisteren eindigde; daarvoor enkel wie eerder eindigde en nog wacht.
        var latestEnd = today.AddDays(TimeOnly.FromDateTime(now.DateTime) >= SendFrom ? -1 : -2);
        var earliestEnd = today.AddDays(-LastDayAfterEnd);
        // Alleen reservaties die eindigden op of na de dag van het aanzetten: oude klanten krijgen niets.
        var switchedOn = DateOnly.FromDateTime(Belgium.Local(since).DateTime);
        if (switchedOn > earliestEnd) earliestEnd = switchedOn;
        if (earliestEnd > latestEnd) return 0;

        var due = await db.Reservations.Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled && r.ReviewRequestMailedAtUtc == null
                && r.EndDate >= earliestEnd && r.EndDate <= latestEnd)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Number)
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        var host = await db.TenantHosts.AsNoTracking().Where(h => h.TenantId == tenant.Id)
            .OrderByDescending(h => h.IsPrimary).Select(h => h.Hostname).FirstOrDefaultAsync(ct);
        if (host is null)
        {
            logger.LogWarning("Verhuurder {Tenant} heeft geen domein: geen reviewvragen.", tenant.Slug);
            return 0;
        }
        var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => t.Kind == MailKind.ReviewRequest).ToListAsync(ct);

        // Eén mail per klant (e-mailadres) en einddag, voor al zijn reservaties die dan eindigden; wie zich afmeldde,
        // krijgt niets. Zijn reservaties blijven zonder reviewvraag, zodat ze ook in de Planning niet meer opduiken.
        var customers = due.Where(r => r.Customer is not null).ToList();
        var optedOut = await optOuts.OptedOutAmongAsync(customers.Select(r => r.Customer!.Email), ct);
        var groups = customers
            .GroupBy(r => (Email: ReviewOptOut.Normalize(r.Customer!.Email), r.EndDate))
            .Where(g => !optedOut.Contains(g.Key.Email))
            .Select(g => g.ToList());

        var sent = 0;
        foreach (var reservations in groups)
        {
            var ids = reservations.Select(r => r.Id).ToList();
            // Eerst opeisen, dan mailen: zo vertrekt er nooit een tweede, ook niet als het versturen twee keer tegelijk
            // loopt. Mislukt de mail, dan wordt de claim vrijgegeven zodat een volgende run het opnieuw probeert.
            var claimed = await db.Reservations
                .Where(r => ids.Contains(r.Id) && r.ReviewRequestMailedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewRequestMailedAtUtc, nowUtc), ct);
            if (claimed == 0) continue;

            var first = reservations[0];
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
            foreach (var reservation in reservations) reservation.RecordReviewRequestMailed(nowUtc);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return sent;
    }
}
