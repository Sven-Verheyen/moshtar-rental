using Moshtar.Domain.Customers;

namespace Moshtar.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0470 12 34 56", "+32470123456")]
    [InlineData("0470/12.34.56", "+32470123456")]
    [InlineData(" 03 123 45 67 ", "+3231234567")]
    [InlineData("(03) 123-45-67", "+3231234567")]
    public void A_Belgian_number_gets_the_Belgian_country_code(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.International(input));

    [Theory]
    [InlineData("+32 470 12 34 56", "+32470123456")]
    [InlineData("0032 470 12 34 56", "+32470123456")]
    [InlineData("+31 6 12345678", "+31612345678")]
    [InlineData("0031 6 12345678", "+31612345678")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    public void A_number_with_a_country_code_keeps_it(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.International(input));

    [Theory]
    [InlineData("0470")]
    [InlineData("0470 12 34 56 78 90")]
    [InlineData("+32")]
    [InlineData("+0 470 12 34 56")]
    [InlineData("00 470")]
    [InlineData("470 12 34 56")]
    [InlineData("0470 12 34 5x")]
    [InlineData("bel me maar")]
    [InlineData("+32 470 12 34 56 of 0475 11 22 33")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Something_that_cannot_be_a_phone_number_is_refused(string? input) =>
        Assert.Null(PhoneNumber.International(input));
}
