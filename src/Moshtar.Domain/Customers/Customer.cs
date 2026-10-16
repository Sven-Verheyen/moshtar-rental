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
    public Address Address { get; set; } = new();
    /// <summary>Een apart factuuradres; null als het hetzelfde is als <see cref="Address"/>.</summary>
    public Address? BillingAddress { get; set; }
    public string PreferredCulture { get; set; } = "nl";
    /// <summary>De interne notitie van de verhuurder, die de klant nooit ziet. Reserveren overschrijft ze nooit.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    /// <summary>De gebruiker die de klant aanmaakte; null als hij "via website" klant werd.</summary>
    public Guid? CreatedByUserId { get; set; }
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
        VatNumber = null;
        Address = new Address { Country = "" };
        BillingAddress = null;
        Notes = null;
        // Elk nieuw persoonsgegeven op de klant wordt hier mee gewist, zoals straks de gegevens van de btw-databank
        // (status, tijdstip, officiële naam en adres).
        RecordChange(userId, atUtc);
    }

    /// <summary>Het adres waarop de klant gefactureerd wordt: zijn factuuradres, of anders zijn adres.</summary>
    public Address BillingAddressOrAddress => BillingAddress ?? Address;

    /// <summary>Een e-mailadres zoals het bewaard en vergeleken wordt: zonder spaties rond en in kleine letters.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Neemt de gegevens over die een gebruiker invulde, de notitie inbegrepen.</summary>
    public void Apply(CustomerDetails details)
    {
        Kind = details.Kind;
        FirstName = details.FirstName.Trim();
        LastName = details.LastName.Trim();
        Email = NormalizeEmail(details.Email);
        Phone = PhoneNumber.International(details.Phone);
        CompanyName = Blank(details.CompanyName);
        VatNumber = Blank(details.VatNumber)?.ToUpperInvariant().Replace(" ", "").Replace(".", "");
        Address = Trimmed(details.Address);
        BillingAddress = details.BillingAddress is null ? null : Trimmed(details.BillingAddress);
        PreferredCulture = details.Culture;
        Notes = Blank(details.Notes);
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
