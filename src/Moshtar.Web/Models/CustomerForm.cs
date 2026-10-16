using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;

namespace Moshtar.Web.Models;

/// <summary>
/// Het formulier van een klant in het back office. De regels staan in <see cref="CustomerDetails.Errors"/>, zodat
/// toevoegen en wijzigen ze altijd toepassen, ook buiten dit formulier.
/// </summary>
public class CustomerForm : ICompanyForm
{
    public CustomerKind Kind { get; set; } = CustomerKind.Private;
    public string? CompanyName { get; set; }
    public string? VatNumber { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Street { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "BE";
    public string Culture { get; set; } = "nl";

    /// <summary>"Factuuradres is hetzelfde", standaard aan.</summary>
    public bool SameBillingAddress { get; set; } = true;
    public string BillingStreet { get; set; } = "";
    public string BillingPostalCode { get; set; } = "";
    public string BillingCity { get; set; } = "";
    public string BillingCountry { get; set; } = "BE";

    public string? Notes { get; set; }

    public static CustomerForm From(Customer c) => new()
    {
        Kind = c.Kind, CompanyName = c.CompanyName, VatNumber = c.VatNumber, FirstName = c.FirstName, LastName = c.LastName,
        Email = c.Email, Phone = c.Phone, Street = c.Address.Street, PostalCode = c.Address.PostalCode, City = c.Address.City,
        Country = c.Address.Country, Culture = c.PreferredCulture, SameBillingAddress = c.BillingAddress is null,
        BillingStreet = c.BillingAddress?.Street ?? "", BillingPostalCode = c.BillingAddress?.PostalCode ?? "",
        BillingCity = c.BillingAddress?.City ?? "", BillingCountry = c.BillingAddress?.Country ?? "BE", Notes = c.Notes,
    };

    public CustomerDetails ToDetails() => new(
        Kind, FirstName, LastName, Email, Phone,
        new Address { Street = Street, PostalCode = PostalCode, City = City, Country = Country },
        Culture,
        // Een particuliere klant heeft geen bedrijfsnaam of btw-nummer, ook niet als hij eerst zakelijk aangeduid stond.
        Kind == CustomerKind.Business ? CompanyName : null,
        Kind == CustomerKind.Business ? VatNumber : null,
        SameBillingAddress ? null : new Address { Street = BillingStreet, PostalCode = BillingPostalCode, City = BillingCity, Country = BillingCountry },
        Notes);
}
