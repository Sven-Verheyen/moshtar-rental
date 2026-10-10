using Moshtar.Domain.Common;

namespace Moshtar.Domain.Mail;

/// <summary>
/// Wat een verhuurder zelf aanpaste aan een mailsjabloon, voor één soort mail en één taal.
/// Zonder aanpassing geldt de standaardtekst van Moshtar; oude versies worden niet bewaard.
/// </summary>
public class MailTemplateCustomization : TenantEntity
{
    public MailKind Kind { get; set; }
    public string Culture { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    /// <summary>De gebruiker die het mailsjabloon laatst aanpaste.</summary>
    public Guid UpdatedByUserId { get; set; }

    public MailTemplateText Text => new(Subject, Body);
}
