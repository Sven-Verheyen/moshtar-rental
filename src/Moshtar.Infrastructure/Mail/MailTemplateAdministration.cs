using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Mail;

internal sealed class MailTemplateAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock) : IMailTemplateAdministration
{
    private const int MaxSubjectLength = 300;

    public async Task<IReadOnlyList<MailTemplateVersion>> GetAsync(MailKind kind, CancellationToken ct = default)
    {
        var definition = MailKinds.For(kind);
        var customizations = await (
            from t in db.MailTemplateCustomizations.AsNoTracking()
            where t.Kind == kind
            join u in db.Users on t.UpdatedByUserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new { t.Culture, t.Subject, t.Body, t.UpdatedAtUtc, Email = u == null ? null : u.Email })
            .ToListAsync(ct);

        return Tenant.Cultures.Select(culture =>
            customizations.SingleOrDefault(c => c.Culture == culture) is { } c
                ? new MailTemplateVersion(kind, culture, new MailTemplateText(c.Subject, c.Body), true, c.UpdatedAtUtc, c.Email)
                : new MailTemplateVersion(kind, culture, definition.Standard(culture), false, null, null))
            .ToList();
    }

    public async Task SaveAsync(MailKind kind, string culture, MailTemplateText text, Guid userId, CancellationToken ct = default)
    {
        var definition = MailKinds.For(kind);
        if (!Tenant.Cultures.Contains(culture))
            throw new MailTemplateException([$"Je biedt de taal '{culture}' niet aan."]);
        text = new MailTemplateText(text.Subject.Trim(), text.Body.Trim().ReplaceLineEndings("\n"));
        var problems = definition.Problems(text).ToList();
        if (text.Subject.Length == 0) problems.Insert(0, "Het onderwerp is leeg.");
        if (text.Subject.Length > MaxSubjectLength) problems.Insert(0, $"Het onderwerp is langer dan {MaxSubjectLength} tekens.");
        if (problems.Count > 0) throw new MailTemplateException(problems);

        // Dezelfde tekst als de standaard is geen aanpassing: zo krijgt de verhuurder later een verbeterde standaardtekst.
        var standard = definition.Standard(culture);
        if (text == standard with { Body = standard.Body.ReplaceLineEndings("\n") })
        {
            await ResetAsync(kind, culture, ct);
            return;
        }

        var customization = await db.MailTemplateCustomizations.SingleOrDefaultAsync(t => t.Kind == kind && t.Culture == culture, ct);
        if (customization is null)
            db.MailTemplateCustomizations.Add(customization = new MailTemplateCustomization { Kind = kind, Culture = culture });
        customization.Subject = text.Subject;
        customization.Body = text.Body;
        customization.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        customization.UpdatedByUserId = userId;
        await db.SaveChangesAsync(ct);
    }

    public Task ResetAsync(MailKind kind, string culture, CancellationToken ct = default) =>
        db.MailTemplateCustomizations.Where(t => t.Kind == kind && t.Culture == culture).ExecuteDeleteAsync(ct);

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
