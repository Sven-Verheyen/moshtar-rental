using Moshtar.Domain.Tenants;

namespace Moshtar.Domain.Tests;

public class BusinessDetailsTests
{
    [Theory]
    [InlineData(" +32 475 12 34 56 ", "+32 475 12 34 56")]
    [InlineData("+32 3 123 45 67", "+32 3 123 45 67")]
    [InlineData("0475 12 34 56", null)]
    [InlineData("+32 475 12 34 56 of 0475", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_an_international_number_is_a_phone_number(string? input, string? expected) =>
        Assert.Equal(expected, BusinessDetails.Phone(input));

    [Fact]
    public void Phone_links_keep_only_the_digits()
    {
        Assert.Equal("+32475123456", BusinessDetails.TelLink("+32 475/12.34.56"));
        Assert.Equal("https://wa.me/32475123456", BusinessDetails.WhatsAppLink("+32 475 12 34 56"));
    }

    [Theory]
    [InlineData(" https://www.facebook.com/hopsakee ", "https://www.facebook.com/hopsakee")]
    [InlineData("http://www.facebook.com/hopsakee", null)]
    [InlineData("facebook.com/hopsakee", null)]
    [InlineData("javascript:alert(1)", null)]
    public void Only_a_full_https_address_is_a_link(string input, string? expected) =>
        Assert.Equal(expected, BusinessDetails.Link(input));

    [Theory]
    [InlineData(" info@hopsakee.fun ", "info@hopsakee.fun")]
    [InlineData("info@hopsakee", null)]
    [InlineData("Info <info@hopsakee.fun>", null)]
    [InlineData("geen adres", null)]
    public void An_email_address_is_one_plain_address(string input, string? expected) =>
        Assert.Equal(expected, BusinessDetails.Email(input));

    [Fact]
    public void The_service_area_is_a_list_of_municipalities_without_blanks_or_doubles() =>
        Assert.Equal(["Antwerpen", "Mortsel", "Edegem"], BusinessDetails.ServiceArea(" Antwerpen\r\nMortsel,  \nEdegem\nantwerpen\n"));

    [Fact]
    public void Empty_fields_are_fine_except_the_email_address()
    {
        Assert.Empty(BusinessDetails.Problems("info@hopsakee.fun", null, "", " ", null, null, null));
        Assert.Equal(["Het e-mailadres is verplicht: klanten antwoorden erop als ze je mails beantwoorden."],
            BusinessDetails.Problems(" ", null, null, null, null, null, null));
    }

    [Fact]
    public void Every_invalid_field_is_named()
    {
        var problems = BusinessDetails.Problems("x", "0475", "0475", "http://fb", "x", "x", "x");

        Assert.Equal(7, problems.Count);
        Assert.Contains("De link naar je Google-bedrijfsprofiel moet een volledig adres zijn dat begint met https://.", problems);
    }
}
