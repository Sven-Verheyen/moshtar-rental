using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Carts;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Mail;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Carts;
using Moshtar.Infrastructure.Reservations;
using Moshtar.Infrastructure.Schedule;

namespace Moshtar.Infrastructure.Mail;

internal sealed class MailTemplateAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock, IMailer mailer) : IMailTemplateAdministration
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
        text = Checked(definition, culture, text);

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

    public Task<MailMessage> PreviewAsync(MailKind kind, string culture, MailTemplateText text, CancellationToken ct = default) =>
        PreviewAsync(kind, culture, text, to: "", ct);

    public async Task SendTestAsync(MailKind kind, string culture, MailTemplateText text, Guid userId, CancellationToken ct = default)
    {
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Geen e-mailadres gekend voor deze gebruiker.");
        var mail = await PreviewAsync(kind, culture, text, email, ct);
        await mailer.SendAsync(mail with { Subject = $"[Testmail] {mail.Subject}" }, ct);
    }

    public Task ResetAsync(MailKind kind, string culture, CancellationToken ct = default) =>
        db.MailTemplateCustomizations.Where(t => t.Kind == kind && t.Culture == culture).ExecuteDeleteAsync(ct);

    private async Task<MailMessage> PreviewAsync(MailKind kind, string culture, MailTemplateText text, string to, CancellationToken ct)
    {
        text = Checked(MailKinds.For(kind), culture, text);
        var host = await db.TenantHosts.AsNoTracking().Where(h => h.TenantId == Tenant.Id)
            .OrderByDescending(h => h.IsPrimary).Select(h => h.Hostname).FirstOrDefaultAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var mail = kind switch
        {
            MailKind.ReservationConfirmation => ReservationConfirmation.Create(ReservationConfirmation.Example(culture, today), Tenant, text),
            MailKind.CartReminder => CartReminder.Create(CartReminder.Example(culture, today), CartReminder.LanguageOf(culture, Tenant), Tenant, text,
                $"https://{host ?? "www.example.com"}{CartReminderPath.For("voorbeeld")}"),
            MailKind.ExpectedDeliveryTime or MailKind.ExpectedCollectionTime => ExpectedTimeMail.Example(kind, culture, today, Tenant, text),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return mail with { To = to };
    }

    /// <summary>De tekst zoals hij bewaard en verstuurd wordt, of een <see cref="MailTemplateException"/> met wat er mis is.</summary>
    private MailTemplateText Checked(MailKindDefinition definition, string culture, MailTemplateText text)
    {
        if (!Tenant.Cultures.Contains(culture))
            throw new MailTemplateException([$"Je biedt de taal '{culture}' niet aan."]);
        text = new MailTemplateText(text.Subject.Trim(), text.Body.Trim().ReplaceLineEndings("\n"));
        var problems = definition.Problems(text).ToList();
        if (text.Subject.Length == 0) problems.Insert(0, "Het onderwerp is leeg.");
        if (text.Subject.Length > MaxSubjectLength) problems.Insert(0, $"Het onderwerp is langer dan {MaxSubjectLength} tekens.");
        if (problems.Count > 0) throw new MailTemplateException(problems);
        return text;
    }

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
