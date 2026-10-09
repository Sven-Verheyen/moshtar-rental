using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;

namespace Moshtar.Infrastructure.Mail;

internal sealed class Mailer(ITenantContext tenantContext, IMailTransport transport) : IMailer
{
    public Task SendAsync(MailMessage message, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend om namens te mailen.");
        if (string.IsNullOrWhiteSpace(tenant.SenderEmail))
            throw new InvalidOperationException($"Verhuurder '{tenant.Slug}' heeft nog geen afzenderadres en kan niet mailen.");

        return transport.SendAsync(new OutgoingMail(
            tenant.SenderEmail, tenant.Name, tenant.ContactEmail,
            message.To, message.Subject, message.HtmlBody, message.TextBody), ct);
    }
}
