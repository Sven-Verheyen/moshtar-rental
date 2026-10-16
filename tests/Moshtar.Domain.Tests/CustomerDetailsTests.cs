using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;

namespace Moshtar.Domain.Tests;

/// <summary>Wat een gebruiker minstens invult als hij een klant toevoegt of wijzigt.</summary>
public class CustomerDetailsTests
{
    private static readonly Address Home = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };

    private static CustomerDetails Private(string first = "Els", string last = "Peeters", string email = "els@example.test",
        string? phone = "0470 12 34 56", Address? address = null, string culture = "nl") =>
        new(CustomerKind.Private, first, last, email, phone, address ?? Home, culture);

    [Fact]
    public void A_complete_private_customer_has_no_errors() =>
        Assert.Empty(Private().Errors());

    [Fact]
    public void Every_required_field_that_is_missing_is_named()
    {
        var errors = Private(first: " ", last: "", email: "", phone: null, address: new Address(), culture: "").Errors();

        Assert.Equal(
        [
            "De voornaam is verplicht.",
            "De achternaam is verplicht.",
            "Het e-mailadres is verplicht.",
            "Het telefoonnummer is verplicht.",
            "Vul de straat met huisnummer, de postcode en de gemeente in.",
            "Kies de taal van de klant.",
        ], errors);
    }

    [Theory]
    [InlineData("geen-apenstaart")]
    [InlineData("twee@@example.test")]
    [InlineData("spatie in@example.test")]
    public void An_email_address_that_cannot_be_one_is_refused(string email) =>
        Assert.Equal(["Dit is geen geldig e-mailadres."], Private(email: email).Errors());

    [Fact]
    public void A_phone_number_that_cannot_be_one_is_refused() =>
        Assert.Equal(["Dit is geen geldig telefoonnummer, bv. 0470 12 34 56 of +32 470 12 34 56."], Private(phone: "0470 12").Errors());

    [Fact]
    public void A_business_customer_needs_a_company_name_but_no_vat_number()
    {
        var withoutCompany = Private() with { Kind = CustomerKind.Business, CompanyName = " " };
        var withCompany = Private() with { Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde" };

        Assert.Equal(["De bedrijfsnaam is verplicht voor een zakelijke klant."], withoutCompany.Errors());
        Assert.Empty(withCompany.Errors());
    }

    [Fact]
    public void A_vat_number_with_a_typo_is_refused_but_one_from_outside_the_eu_is_accepted()
    {
        var business = Private() with { Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde" };

        Assert.Equal(["Dit is geen geldig btw-nummer. Begin met de landcode, bv. BE0123456789."], (business with { VatNumber = "BE 123.456.789" }).Errors());
        Assert.Empty((business with { VatNumber = "BE 0123.456.789" }).Errors());
        Assert.Empty((business with { VatNumber = "CHE-123.456.789" }).Errors());
    }

    [Fact]
    public void A_separate_billing_address_must_be_complete()
    {
        var incomplete = Private() with { BillingAddress = new Address { Street = "Schoolstraat 3" } };

        Assert.Equal(["Vul de straat met huisnummer, de postcode en de gemeente van het factuuradres in."], incomplete.Errors());
    }

    [Fact]
    public void The_billing_address_is_the_address_unless_a_separate_one_is_given()
    {
        var billing = new Address { Street = "Schoolstraat 3", PostalCode = "9000", City = "Gent" };

        Assert.Same(Home, new Customer { Address = Home }.BillingAddressOrAddress);
        Assert.Same(billing, new Customer { Address = Home, BillingAddress = billing }.BillingAddressOrAddress);
    }
}
