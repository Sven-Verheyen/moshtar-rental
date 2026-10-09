namespace Moshtar.Application.Mail;

/// <summary>
/// Verstuurt e-mail namens de verhuurder van het huidige request, vanaf zijn eigen domein
/// en met zijn contactadres als antwoordadres (zie ADR 0002).
/// </summary>
public interface IMailer
{
    Task SendAsync(MailMessage message, CancellationToken ct = default);
}

/// <summary>Een mail aan één ontvanger, met een HTML- en een tekstversie.</summary>
public record MailMessage(string To, string Subject, string HtmlBody, string TextBody);
