using Moshtar.Domain.Customers;

namespace Moshtar.Domain.Tests;

/// <summary>
/// Het adres dat de btw-databank geeft, is één vrije tekst, vaak over meerdere regels. Moshtar haalt er zo goed als het
/// kan straat, postcode en gemeente uit om het factuuradres in te vullen; de gebruiker kan het nog aanpassen.
/// </summary>
public class VatRegistryAddressTests
{
    [Theory]
    [InlineData("Kerkstraat 1\n2000 Antwerpen", "BE", "Kerkstraat 1", "2000", "Antwerpen", "BE")]
    [InlineData("Rue de la Loi 200\r\n1000 Bruxelles\n", "BE", "Rue de la Loi 200", "1000", "Bruxelles", "BE")]
    [InlineData("Kerkstraat 1, 2000 Antwerpen", "BE", "Kerkstraat 1", "2000", "Antwerpen", "BE")]
    [InlineData("Damrak 1\n1012 LG Amsterdam", "NL", "Damrak 1", "1012 LG", "Amsterdam", "NL")]
    [InlineData("SCHOOLSTRAAT 3 BUS 2\nBLOK A\n9050 GENTBRUGGE", "BE", "SCHOOLSTRAAT 3 BUS 2, BLOK A", "9050", "GENTBRUGGE", "BE")]
    [InlineData("ODOS 1\n10431 ATHINA", "EL", "ODOS 1", "10431", "ATHINA", "GR")]
    public void The_street_postal_code_and_city_come_from_the_official_address(
        string text, string vatCountry, string street, string postalCode, string city, string country)
    {
        var address = VatRegistryAddress.Parse(text, vatCountry);

        Assert.NotNull(address);
        Assert.Equal((street, postalCode, city, country), (address.Street, address.PostalCode, address.City, address.Country));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Kerkstraat 1")]
    public void An_address_without_a_postal_code_and_city_cannot_be_used(string? text) =>
        Assert.Null(VatRegistryAddress.Parse(text, "BE"));
}
