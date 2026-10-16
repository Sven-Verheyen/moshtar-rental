using System.ComponentModel.DataAnnotations;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Models;

/// <summary>Een reservatie die een gebruiker in het back office invoert.</summary>
public class ManualReservationForm : ICompanyForm
{
    [Required(ErrorMessage = "Kies een begindatum.")] public DateTime? StartDate { get; set; }
    [Required(ErrorMessage = "Kies een einddatum.")] public DateTime? EndDate { get; set; }

    public List<ManualReservationLine> Lines { get; set; } = [new()];

    /// <summary>De bestaande klant die gekozen werd, om zijn klantfiche te tonen; null voor een nieuwe klant.</summary>
    public Guid? CustomerId { get; set; }
    public CustomerKind Kind { get; set; } = CustomerKind.Private;
    [MaxLength(200)] public string? CompanyName { get; set; }
    [MaxLength(30)] public string? VatNumber { get; set; }

    [Required(ErrorMessage = "Vul de voornaam in."), MaxLength(100)] public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "Vul de achternaam in."), MaxLength(100)] public string LastName { get; set; } = "";
    [Required(ErrorMessage = "Vul het e-mailadres in."), EmailAddress(ErrorMessage = "Dit is geen geldig e-mailadres."), MaxLength(320)]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Vul het telefoonnummer in."), MaxLength(30)] public string? Phone { get; set; }

    [Required(ErrorMessage = "Vul de straat en het huisnummer in."), MaxLength(200)] public string Street { get; set; } = "";
    [Required(ErrorMessage = "Vul de postcode in."), MaxLength(10)] public string PostalCode { get; set; } = "";
    [Required(ErrorMessage = "Vul de gemeente in."), MaxLength(100)] public string City { get; set; } = "";

    public string Country { get; set; } = "BE";

    /// <summary>"Factuuradres is hetzelfde", standaard aan.</summary>
    public bool SameBillingAddress { get; set; } = true;
    [MaxLength(200)] public string BillingStreet { get; set; } = "";
    [MaxLength(10)] public string BillingPostalCode { get; set; } = "";
    [MaxLength(100)] public string BillingCity { get; set; } = "";
    public string BillingCountry { get; set; } = "BE";

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    /// <summary>Leveren op het adres van de klant, standaard aan; anders op een eigen leveradres voor deze reservatie.</summary>
    public bool DeliverToCustomerAddress { get; set; } = true;
    [MaxLength(200)] public string DeliveryStreet { get; set; } = "";
    [MaxLength(10)] public string DeliveryPostalCode { get; set; } = "";
    [MaxLength(100)] public string DeliveryCity { get; set; } = "";
    [MaxLength(2000)] public string? Notes { get; set; }
    /// <summary>De taal van de klant, voor de bevestigingsmail.</summary>
    public string Culture { get; set; } = "nl";

    /// <summary>Vult de gegevens van een bestaande klant in, zoals ze nu op zijn klantfiche staan.</summary>
    public void Fill(Customer c)
    {
        CustomerId = c.Id;
        Kind = c.Kind;
        CompanyName = c.CompanyName;
        VatNumber = c.VatNumber;
        FirstName = c.FirstName;
        LastName = c.LastName;
        Email = c.Email;
        Phone = c.Phone;
        Street = c.Address.Street;
        PostalCode = c.Address.PostalCode;
        City = c.Address.City;
        Country = c.Address.Country;
        SameBillingAddress = c.BillingAddress is null;
        BillingStreet = c.BillingAddress?.Street ?? "";
        BillingPostalCode = c.BillingAddress?.PostalCode ?? "";
        BillingCity = c.BillingAddress?.City ?? "";
        BillingCountry = c.BillingAddress?.Country ?? "BE";
        Culture = c.PreferredCulture;
    }

    public Address Address => new() { Street = Street, PostalCode = PostalCode, City = City, Country = Country };

    /// <summary>De klant voor de reservatie; een particuliere klant zonder bedrijfsnaam of btw-nummer.</summary>
    public ReservationCustomer Customer() => new(
        FirstName, LastName, Email, Phone, Address, Kind,
        Kind == CustomerKind.Business ? CompanyName : null,
        Kind == CustomerKind.Business ? VatNumber : null,
        SameBillingAddress ? null : new Address { Street = BillingStreet.Trim(), PostalCode = BillingPostalCode.Trim(), City = BillingCity.Trim(), Country = BillingCountry });

    /// <summary>Het leveradres van deze reservatie: het adres van de klant, of een eigen leveradres.</summary>
    public Address DeliveryAddress => DeliverToCustomerAddress
        ? Address
        : new Address { Street = DeliveryStreet.Trim(), PostalCode = DeliveryPostalCode.Trim(), City = DeliveryCity.Trim() };
}

public class ManualReservationLine
{
    /// <summary>Het gekozen artikel of pakket, als "item:{id}" of "bundle:{id}".</summary>
    public string? Choice { get; set; }
    public int Quantity { get; set; } = 1;
}
