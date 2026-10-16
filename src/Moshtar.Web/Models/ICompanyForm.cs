using Moshtar.Application.Customers;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;

namespace Moshtar.Web.Models;

/// <summary>
/// Een formulier met de bedrijfsgegevens en het (factuur)adres van een klant: de klantfiche en een nieuwe reservatie in
/// het back office. Een ingegeven btw-nummer vult er bedrijfsnaam en factuuradres in (<see cref="CompanyFormFill.Fill"/>).
/// </summary>
public interface ICompanyForm
{
    string? CompanyName { get; set; }
    string? VatNumber { get; set; }
    string Street { get; }
    string PostalCode { get; }
    string City { get; }
    bool SameBillingAddress { get; set; }
    string BillingStreet { get; set; }
    string BillingPostalCode { get; set; }
    string BillingCity { get; set; }
    string BillingCountry { get; set; }
}

public static class CompanyFormFill
{
    /// <summary>
    /// Vult de bedrijfsnaam en het factuuradres in met wat de btw-databank over een geldig nummer zegt, voor zover ze het
    /// geeft; de gebruiker kan ze daarna nog aanpassen. Is het officiële adres het adres van de klant, dan blijft
    /// "Factuuradres is hetzelfde" aan.
    /// </summary>
    public static void Fill(this ICompanyForm form, VatNumberLookup lookup)
    {
        if (lookup.Status != VatCheckStatus.Valid) return;
        if (!string.IsNullOrWhiteSpace(lookup.Name)) form.CompanyName = lookup.Name;
        if (lookup.Address is not { } official || IsAddress(form, official)) return;
        form.SameBillingAddress = false;
        form.BillingStreet = official.Street;
        form.BillingPostalCode = official.PostalCode;
        form.BillingCity = official.City;
        form.BillingCountry = official.Country;
    }

    private static bool IsAddress(ICompanyForm form, Address a) =>
        string.Equals(a.Street.Trim(), form.Street.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.PostalCode.Trim(), form.PostalCode.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.City.Trim(), form.City.Trim(), StringComparison.OrdinalIgnoreCase);
}
