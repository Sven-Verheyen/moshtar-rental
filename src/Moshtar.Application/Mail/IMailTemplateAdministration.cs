using Moshtar.Domain.Mail;

namespace Moshtar.Application.Mail;

/// <summary>Een mailsjabloon in één taal zoals de beheerder het ziet: de aanpassing van de verhuurder, of de standaardtekst.</summary>
public sealed record MailTemplateVersion(
    MailKind Kind,
    string Culture,
    MailTemplateText Text,
    bool IsCustomized,
    DateTime? UpdatedAtUtc,
    string? UpdatedByEmail);

/// <summary>Een mailsjabloon kan zo niet bewaard worden; de boodschap is voor de beheerder bedoeld.</summary>
public sealed class MailTemplateException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>Beheer van de mailsjablonen van de verhuurder van het huidige request (ADR 0003).</summary>
public interface IMailTemplateAdministration
{
    /// <summary>Het mailsjabloon in elke taal die de verhuurder aanbiedt.</summary>
    Task<IReadOnlyList<MailTemplateVersion>> GetAsync(MailKind kind, CancellationToken ct = default);

    /// <summary>
    /// Bewaart de tekst als aanpassing van de verhuurder. Gooit een <see cref="MailTemplateException"/> als er
    /// iets mis is, en bewaart dan niets. Een tekst gelijk aan de standaardtekst is geen aanpassing.
    /// </summary>
    Task SaveAsync(MailKind kind, string culture, MailTemplateText text, Guid userId, CancellationToken ct = default);

    /// <summary>Verwijdert de aanpassing in deze taal, zodat de standaardtekst weer geldt.</summary>
    Task ResetAsync(MailKind kind, string culture, CancellationToken ct = default);

    /// <summary>
    /// De mail zoals een klant hem zou krijgen met deze (nog niet bewaarde) tekst, ingevuld met een fictieve reservatie.
    /// Gooit een <see cref="MailTemplateException"/> bij dezelfde problemen als <see cref="SaveAsync"/>.
    /// </summary>
    Task<MailMessage> PreviewAsync(MailKind kind, string culture, MailTemplateText text, CancellationToken ct = default);

    /// <summary>Stuurt het voorbeeld als testmail naar de gebruiker zelf, vanaf het domein van de verhuurder.</summary>
    Task SendTestAsync(MailKind kind, string culture, MailTemplateText text, Guid userId, CancellationToken ct = default);
}
