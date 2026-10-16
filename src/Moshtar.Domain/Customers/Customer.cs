using Moshtar.Domain.Common;

namespace Moshtar.Domain.Customers;

/// <summary>
/// Een klant van een verhuurder: wie reserveerde (ook als die reservatie geannuleerd werd) of wie een gebruiker zelf
/// toevoegde. Herkend aan zijn e-mailadres, uniek per verhuurder, zoals <see cref="NormalizeEmail"/> het schrijft.
/// </summary>
public class Customer : TenantEntity
{
    public CustomerKind Kind { get; set; } = CustomerKind.Private;
    /// <summary>Voor een zakelijke klant de voornaam van de contactpersoon.</summary>
    public string FirstName { get; set; } = "";
    /// <summary>Voor een zakelijke klant de achternaam van de contactpersoon.</summary>
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string? VatNumber { get; set; }

    /// <summary>Wat de btw-databank over <see cref="VatNumber"/> zei; null zonder btw-nummer. Zie <see cref="ChangeVatNumber"/>.</summary>
    public VatCheckStatus? VatCheck { get; set; }
    /// <summary>
    /// Wanneer de btw-databank het laatst gevraagd werd, ook als ze onbereikbaar was; null als dat nog nooit gebeurde.
    /// </summary>
    public DateTime? VatCheckedAtUtc { get; set; }
    /// <summary>De officiële naam volgens de btw-databank, los van wat de klant of gebruiker als bedrijfsnaam invulde.</summary>
    public string? VatRegistryName { get; set; }
    /// <summary>Het officiële adres volgens de btw-databank, als vrije tekst zoals ze het gaf (zie <see cref="VatRegistryAddress"/>).</summary>
    public string? VatRegistryAddress { get; set; }

    public Address Address { get; set; } = new();
    /// <summary>Een apart factuuradres; null als het hetzelfde is als <see cref="Address"/>.</summary>
    public Address? BillingAddress { get; set; }
    public string PreferredCulture { get; set; } = "nl";
    /// <summary>De interne notitie van de verhuurder, die de klant nooit ziet. Reserveren overschrijft ze nooit.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    /// <summary>De gebruiker die de klant aanmaakte; null als hij "via website" klant werd.</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>Kwam de klant binnen via een import (door <see cref="CreatedByUserId"/>), in plaats van manueel of via de website?</summary>
    public bool CreatedByImport { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    /// <summary>De gebruiker die de klant het laatst wijzigde; null als dat "via website" gebeurde.</summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Het domein van het e-mailadres dat een geanonimiseerde klant krijgt. Het is voorbehouden (RFC 2606): geen
    /// echte klant heeft het en er vertrekt nooit een mail naartoe.
    /// </summary>
    public const string AnonymizedEmailDomain = "geanonimiseerd.invalid";

    /// <summary>Zijn de persoonsgegevens van deze klant gewist? Zie <see cref="Anonymize"/>.</summary>
    public bool IsAnonymized => IsAnonymizedEmail(Email);

    /// <summary>Is dit het e-mailadres van een geanonimiseerde klant?</summary>
    public static bool IsAnonymizedEmail(string email) => email.EndsWith("@" + AnonymizedEmailDomain, StringComparison.Ordinal);

    /// <summary>
    /// Wist de persoonsgegevens van de klant, bijvoorbeeld op zijn vraag: naam, bedrijfsnaam, btw-nummer, e-mailadres,
    /// telefoon, adressen en notitie. Het e-mailadres wordt een uniek adres dat niemand heeft, zodat het oude adres weer
    /// vrij is voor een nieuwe klant. Het type en de taal blijven, voor de cijfers.
    /// </summary>
    public void Anonymize(Guid userId, DateTime atUtc)
    {
        FirstName = "";
        LastName = "";
        Email = $"klant-{Id:N}@{AnonymizedEmailDomain}";
        Phone = null;
        CompanyName = null;
        ChangeVatNumber(null);
        Address = new Address { Country = "" };
        BillingAddress = null;
        Notes = null;
        // Elk nieuw persoonsgegeven op de klant wordt hier mee gewist.
        RecordChange(userId, atUtc);
    }

    /// <summary>Het adres waarop de klant gefactureerd wordt: zijn factuuradres, of anders zijn adres.</summary>
    public Address BillingAddressOrAddress => BillingAddress ?? Address;

    /// <summary>Een e-mailadres zoals het bewaard en vergeleken wordt: zonder spaties rond en in kleine letters.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Neemt de gegevens over die een gebruiker invulde, de notitie inbegrepen. Een particulier houdt nooit een
    /// bedrijfsnaam of btw-nummer, ook niet als die toch ingevuld werden.
    /// </summary>
    public void Apply(CustomerDetails details)
    {
        Kind = details.Kind;
        FirstName = details.FirstName.Trim();
        LastName = details.LastName.Trim();
        Email = NormalizeEmail(details.Email);
        Phone = PhoneNumber.International(details.Phone);
        var business = details.Kind == CustomerKind.Business;
        CompanyName = business ? Blank(details.CompanyName) : null;
        ChangeVatNumber(business ? details.VatNumber : null);
        Address = Trimmed(details.Address);
        BillingAddress = details.BillingAddress is null ? null : Trimmed(details.BillingAddress);
        PreferredCulture = details.Culture;
        Notes = Blank(details.Notes);
    }

    /// <summary>
    /// Zet het btw-nummer. Een ander nummer dan het vorige verliest wat de btw-databank over het vorige zei: zonder
    /// nummer is er geen status, een nummer van buiten de EU is niet te controleren, een EU-nummer is nog niet
    /// gecontroleerd tot <see cref="RecordVatCheck"/>. Hetzelfde nummer houdt zijn status.
    /// </summary>
    public void ChangeVatNumber(string? vatNumber)
    {
        var normalized = VatNumberFormat.Normalize(vatNumber);
        if (normalized == VatNumber && (normalized is null) == (VatCheck is null)) return;
        VatNumber = normalized;
        VatCheck = normalized is null ? null : VatNumberFormat.IsEu(normalized) ? VatCheckStatus.NotChecked : VatCheckStatus.NotCheckable;
        VatCheckedAtUtc = null;
        VatRegistryName = null;
        VatRegistryAddress = null;
    }

    /// <summary>Moet het btw-nummer (nog) bij de btw-databank gecontroleerd worden?</summary>
    public bool AwaitsVatCheck => VatCheck == VatCheckStatus.NotChecked && VatNumber is not null && VatNumberFormat.IsEu(VatNumber);

    /// <summary>
    /// Bewaart wat de btw-databank over het EU-btw-nummer zei, met de officiële naam en het adres apart: de bedrijfsnaam
    /// en het factuuradres van de klant blijven wat hij invulde. Was ze onbereikbaar, dan blijft een eerdere uitspraak
    /// over hetzelfde nummer staan; zonder eerdere uitspraak is het nummer nog niet gecontroleerd.
    /// </summary>
    public void RecordVatCheck(VatRegistryAnswer answer, DateTime atUtc)
    {
        if (VatNumber is null || !VatNumberFormat.IsEu(VatNumber))
            throw new InvalidOperationException("Alleen een EU-btw-nummer wordt bij de btw-databank gecontroleerd.");
        if (answer.Outcome == VatRegistryOutcome.Unavailable)
        {
            if (VatCheck is VatCheckStatus.Valid or VatCheckStatus.Invalid) return;
            VatCheck = VatCheckStatus.NotChecked;
            VatCheckedAtUtc = atUtc;
            return;
        }
        VatCheck = answer.Outcome == VatRegistryOutcome.Valid ? VatCheckStatus.Valid : VatCheckStatus.Invalid;
        VatCheckedAtUtc = atUtc;
        VatRegistryName = answer.Name;
        VatRegistryAddress = answer.Address;
    }

    /// <summary>Wanneer en door wie (null: "via website") de klant aangemaakt of gewijzigd werd.</summary>
    public void RecordChange(Guid? userId, DateTime atUtc)
    {
        if (CreatedAtUtc == default)
        {
            CreatedAtUtc = atUtc;
            CreatedByUserId = userId;
        }
        UpdatedAtUtc = atUtc;
        UpdatedByUserId = userId;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static Address Trimmed(Address a) => new()
    {
        Street = a.Street.Trim(), PostalCode = a.PostalCode.Trim(), City = a.City.Trim(),
        Country = string.IsNullOrWhiteSpace(a.Country) ? "BE" : a.Country.Trim().ToUpperInvariant(),
    };
}
