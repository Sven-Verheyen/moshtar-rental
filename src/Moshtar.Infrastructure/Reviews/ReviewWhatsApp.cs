using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Reviews;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Reviews;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Reviews;

/// <summary>Met een eigen, kortlevende context per oproep, zoals de rest van de Planning.</summary>
internal sealed class ReviewWhatsApp(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock) : IReviewWhatsApp
{
    /// <summary>Hoeveel dagen na de einddag een klant in het blok blijft, zoals de mail.</summary>
    private const int LastDayAfterEnd = 3;

    private const string NotInBlock = "Deze klant staat niet (meer) bij de reviewvragen van deze dag. Laad de Planning opnieuw.";

    public async Task<IReadOnlyList<ReviewWhatsAppRequest>> ForDayAsync(DateOnly day, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return (await DueAsync(db, day, ct)).Select(g => g.Request).ToList();
    }

    public async Task RecordAsync(DateOnly day, Guid reservationId, ReservationActor actor, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Twee gebruikers die tegelijk drukken, geven samen één lijn in de historiek.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"reviewvraag-whatsapp:" + tenantContext.TenantId}, 0))", ct);
        var group = (await DueAsync(db, day, ct)).SingleOrDefault(g => g.Ids.Contains(reservationId))
            ?? throw new InvalidOperationException(NotInBlock);
        if (group.Request.WhatsAppAt is not null) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var reservations = await db.Reservations.Where(r => group.Ids.Contains(r.Id)).ToListAsync(ct);
        foreach (var reservation in reservations) reservation.RecordReviewRequestWhatsApp(actor, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private sealed record Group(ReviewWhatsAppRequest Request, IReadOnlyList<Guid> Ids);

    private async Task<IReadOnlyList<Group>> DueAsync(AppDbContext db, DateOnly day, CancellationToken ct)
    {
        if (day > Belgium.Today(clock)) return [];
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);
        if (tenant?.ReviewRequestsSince is not { } since || BusinessDetails.Link(tenant.ReviewLink) is not { } reviewLink)
            return [];

        var earliestEnd = day.AddDays(-LastDayAfterEnd);
        // Alleen reservaties die eindigden op of na de dag van het aanzetten, zoals de mail.
        var switchedOn = DateOnly.FromDateTime(Belgium.Local(since).DateTime);
        if (switchedOn > earliestEnd) earliestEnd = switchedOn;
        var latestEnd = day.AddDays(-1);
        if (earliestEnd > latestEnd) return [];

        var reservations = await db.Reservations.AsNoTracking().Include(r => r.Customer)
            .Where(r => r.Status != ReservationStatus.Cancelled && r.Customer != null && r.EndDate >= earliestEnd && r.EndDate <= latestEnd)
            .OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Number)
            .ToListAsync(ct);
        if (reservations.Count == 0) return [];

        var emails = reservations.Select(r => ReviewOptOut.Normalize(r.Customer!.Email)).Distinct().ToList();
        var optedOut = (await db.ReviewOptOuts.AsNoTracking().Where(o => emails.Contains(o.Email)).Select(o => o.Email).ToListAsync(ct)).ToHashSet();
        var customizations = await db.MailTemplateCustomizations.AsNoTracking().Where(t => t.Kind == MailKind.ReviewRequest).ToListAsync(ct);

        var groups = new List<Group>();
        foreach (var group in reservations
            .GroupBy(r => (Email: ReviewOptOut.Normalize(r.Customer!.Email), r.EndDate))
            .Where(g => !optedOut.Contains(g.Key.Email)))
        {
            var clicked = group.Max(r => r.ReviewRequestWhatsAppAtUtc) is { } atUtc
                ? new DateTimeOffset(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc))
                : (DateTimeOffset?)null;
            // Wie aangeklikt werd, blijft die dag staan met het tijdstip en verdwijnt de dag erna.
            if (clicked is { } at && DateOnly.FromDateTime(Belgium.Local(at).DateTime) < day) continue;

            var first = group.First();
            var customer = first.Customer!;
            var language = ReservationConfirmation.LanguageOf(first, tenant);
            var template = ReviewRequestMail.Template(customizations.SingleOrDefault(c => c.Culture == language), language);
            var link = PhoneNumber.International(customer.Phone) is { } phone
                ? $"{BusinessDetails.WhatsAppLink(phone)}?text={Uri.EscapeDataString(ReviewRequestMail.WhatsApp(first, tenant, template, reviewLink))}"
                : null;
            groups.Add(new Group(
                new ReviewWhatsAppRequest(first.Id, first.Number, group.Select(r => r.Number).ToList(),
                    $"{customer.FirstName} {customer.LastName}".Trim(), customer.Phone, group.Key.EndDate, link, clicked),
                group.Select(r => r.Id).ToList()));
        }
        return groups.OrderBy(g => g.Request.EndDate).ThenBy(g => g.Request.Number).ToList();
    }
}
