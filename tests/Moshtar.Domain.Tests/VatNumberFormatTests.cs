using Moshtar.Domain.Customers;

namespace Moshtar.Domain.Tests;

/// <summary>
/// Het formaat van een btw-nummer, gecontroleerd voor er iets naar de btw-databank gaat, zodat een tikfout meteen
/// opvalt. Een nummer uit een EU-land volgt het formaat van dat land; een nummer van buiten de EU is niet te controleren.
/// </summary>
public class VatNumberFormatTests
{
    [Theory]
    [InlineData("BE0123456789", "BE0123456789")]
    [InlineData(" be 0123.456.789 ", "BE0123456789")]
    [InlineData("BE-0123-456-789", "BE0123456789")]
    [InlineData("nl123456789b01", "NL123456789B01")]
    [InlineData("GR123456789", "EL123456789")]
    public void A_vat_number_is_written_in_capitals_without_separators(string input, string expected) =>
        Assert.Equal(expected, VatNumberFormat.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_vat_number_stays_empty(string? input) =>
        Assert.Null(VatNumberFormat.Normalize(input));

    [Theory]
    [InlineData("BE0123456789")]
    [InlineData("BE1123456789")]
    [InlineData("NL123456789B01")]
    [InlineData("FR12345678901")]
    [InlineData("FRXX123456789")]
    [InlineData("DE123456789")]
    [InlineData("LU12345678")]
    [InlineData("ATU12345678")]
    [InlineData("ESX1234567X")]
    [InlineData("EL123456789")]
    [InlineData("IE1234567T")]
    [InlineData("IE1234567TW")]
    [InlineData("XI123456789")]
    public void An_eu_vat_number_in_the_format_of_its_country_is_well_formed(string number)
    {
        Assert.True(VatNumberFormat.IsWellFormed(number));
        Assert.True(VatNumberFormat.IsEu(number));
    }

    [Theory]
    [InlineData("BE123456789")]
    [InlineData("BE01234567890")]
    [InlineData("BE2123456789")]
    [InlineData("NL123456789")]
    [InlineData("DE12345678")]
    [InlineData("LU1234567X")]
    [InlineData("AT12345678")]
    public void An_eu_vat_number_with_a_typo_is_not_well_formed(string number) =>
        Assert.False(VatNumberFormat.IsWellFormed(number));

    [Theory]
    [InlineData("CHE123456789")]
    [InlineData("GB123456789")]
    [InlineData("NO123456789MVA")]
    public void A_vat_number_from_outside_the_eu_is_accepted_without_checking_its_format(string number)
    {
        Assert.True(VatNumberFormat.IsWellFormed(number));
        Assert.False(VatNumberFormat.IsEu(number));
    }

    [Theory]
    [InlineData("0123456789")]
    [InlineData("B")]
    [InlineData("BE")]
    [InlineData("??0123456789")]
    public void A_vat_number_without_a_country_code_is_not_well_formed(string number) =>
        Assert.False(VatNumberFormat.IsWellFormed(number));
}
