using System.Collections.Concurrent;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Moshtar.Infrastructure.Mail;

/// <summary>Een volledig geadresseerde mail, klaar om te versturen.</summary>
public record OutgoingMail(
    string FromAddress, string FromName, string ReplyTo,
    string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Het kanaal waarlangs mails echt vertrekken.</summary>
public interface IMailTransport
{
    Task SendAsync(OutgoingMail mail, CancellationToken ct = default);
}

public class MailOptions
{
    /// <summary>
    /// Connection string van Azure Communication Services. Leeg: mails worden niet verstuurd
    /// maar enkel gelogd en bijgehouden (ontwikkeling en tests).
    /// </summary>
    public string? AzureCommunicationServicesConnectionString { get; set; }
}

/// <summary>
/// Verstuurt via Azure Communication Services. Het domein van de afzender moet daar geverifieerd zijn;
/// de afzendernaam hoort bij het afzenderadres en wordt ook daar ingesteld.
/// </summary>
internal sealed class AzureMailTransport(IOptions<MailOptions> options) : IMailTransport
{
    private readonly EmailClient _client = new(options.Value.AzureCommunicationServicesConnectionString);

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
    {
        var message = new EmailMessage(
            mail.FromAddress,
            new EmailRecipients([new EmailAddress(mail.To)]),
            new EmailContent(mail.Subject) { Html = mail.HtmlBody, PlainText = mail.TextBody });
        message.ReplyTo.Add(new EmailAddress(mail.ReplyTo, mail.FromName));
        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}

/// <summary>Verstuurt niets: logt de mail en houdt ze bij, zodat tests kunnen nagaan wat vertrok.</summary>
public sealed class RecordingMailTransport(ILogger<RecordingMailTransport> logger, bool logBody) : IMailTransport
{
    private readonly ConcurrentQueue<OutgoingMail> _sent = new();

    public IReadOnlyCollection<OutgoingMail> Sent => _sent.ToArray();

    public Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
    {
        _sent.Enqueue(mail);
        // Lokaal de volledige tekst, zodat je de links uit uitnodigingen kan volgen. Elders nooit:
        // zo'n link geeft toegang tot een account.
        if (logBody)
            logger.LogInformation("Mail niet echt verstuurd: {From} → {To}: {Subject}\n{Body}",
                mail.FromAddress, mail.To, mail.Subject, mail.TextBody);
        else
            logger.LogWarning("Mail niet verstuurd, want Azure Communication Services is niet ingesteld: {From} → {To}: {Subject}",
                mail.FromAddress, mail.To, mail.Subject);
        return Task.CompletedTask;
    }
}
