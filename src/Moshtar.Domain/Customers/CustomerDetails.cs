using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;

namespace Moshtar.Domain.Customers;

/// <summary>
/// De gegevens van een klant zoals een gebruiker ze in het back office invult. Voor een zakelijke klant zijn voornaam en
/// achternaam die van de contactpersoon. <see cref="BillingAddress"/> null betekent "Factuuradres is hetzelfde".
/// </summary>
public sealed record CustomerDetails(
    CustomerKind Kind,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    Address Address,
    string Culture,
    string? CompanyName = null,
    string? VatNumber = null,
    Address? BillingAddress = null,
    string? Notes = null)
{
    public const string InvalidVatNumber = "Dit is geen geldig btw-nummer. Begin met de landcode, bv. BE0123456789.";
    public const string UnknownVatNumber = "Dit btw-nummer is ongeldig: de btw-databank van de EU kent het niet. Verbeter het of laat het leeg.";
    /// <summary>Hoe lang een e-mailadres in het klantenbestand mag zijn.</summary>
    public const int MaxEmailLength = 320;
    /// <summary>Hoe lang de interne notitie mag zijn.</summary>
    public const int MaxNotesLength = 4000;

    /// <summary>Wat er ontbreekt of niet klopt, in de volgorde van het formulier; leeg als alles in orde is.</summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>(BusinessErrors(Kind, CompanyName, VatNumber));
        if (string.IsNullOrWhiteSpace(FirstName)) errors.Add("De voornaam is verplicht.");
        if (string.IsNullOrWhiteSpace(LastName)) errors.Add("De achternaam is verplicht.");
        if (string.IsNullOrWhiteSpace(Email)) errors.Add("Het e-mailadres is verplicht.");
        else if (Email.Trim().Length > MaxEmailLength) errors.Add($"Het e-mailadres is te lang: hoogstens {MaxEmailLength} tekens.");
        else if (BusinessDetails.Email(Email) is null) errors.Add("Dit is geen geldig e-mailadres.");
        if (string.IsNullOrWhiteSpace(Phone)) errors.Add("Het telefoonnummer is verplicht.");
        else if (PhoneNumber.International(Phone) is null) errors.Add("Dit is geen geldig telefoonnummer, bv. 0470 12 34 56 of +32 470 12 34 56.");
        if (!IsComplete(Address)) errors.Add("Vul de straat met huisnummer, de postcode en de gemeente in.");
        if (BillingAddress is not null && !IsComplete(BillingAddress)) errors.Add(IncompleteBillingAddress);
        if (string.IsNullOrWhiteSpace(Culture)) errors.Add("Kies de taal van de klant.");
        if (Notes?.Trim().Length > MaxNotesLength) errors.Add($"De notitie is te lang: hoogstens {MaxNotesLength} tekens.");
        return errors;
    }

    public const string IncompleteBillingAddress = "Vul de straat met huisnummer, de postcode en de gemeente van het factuuradres in.";

    /// <summary>
    /// Wat er niet klopt aan de bedrijfsnaam en het btw-nummer: een zakelijke klant heeft een bedrijfsnaam nodig en een
    /// ingevuld btw-nummer moet er een zijn. Een particulier houdt geen van beide (<see cref="Customer.Apply"/>), dus
    /// voor hem klopt het altijd.
    /// </summary>
    public static IReadOnlyList<string> BusinessErrors(CustomerKind kind, string? companyName, string? vatNumber)
    {
        if (kind != CustomerKind.Business) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(companyName)) errors.Add("De bedrijfsnaam is verplicht voor een zakelijke klant.");
        if (!string.IsNullOrWhiteSpace(vatNumber) && !VatNumberFormat.IsWellFormed(vatNumber)) errors.Add(InvalidVatNumber);
        return errors;
    }

    /// <summary>Zijn de straat met huisnummer, de postcode en de gemeente ingevuld?</summary>
    public static bool IsComplete(Address address) =>
        !string.IsNullOrWhiteSpace(address.Street) && !string.IsNullOrWhiteSpace(address.PostalCode) && !string.IsNullOrWhiteSpace(address.City);
}
