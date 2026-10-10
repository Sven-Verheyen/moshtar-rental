using Moshtar.Domain.Tenants;

namespace Moshtar.Domain.Tests;

public class BrandingTests
{
    [Theory]
    [InlineData(" https://hopsakee.fun/logo.png ", "https://hopsakee.fun/logo.png")]
    [InlineData("http://hopsakee.fun/logo.png", null)]
    [InlineData("/logo.png", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_a_full_https_address_is_a_logo(string? input, string? expected) =>
        Assert.Equal(expected, Branding.LogoUrl(input));

    [Theory]
    [InlineData("#E91E63", "#e91e63")]
    [InlineData(" #f60 ", "#f60")]
    [InlineData("#e91e6", null)]
    [InlineData("red", null)]
    [InlineData("#123;background:url(x)", null)]
    [InlineData(null, null)]
    public void Only_a_hex_code_is_a_colour(string? input, string? expected) =>
        Assert.Equal(expected, Branding.Color(input));

    [Fact]
    public void Empty_values_are_fine_and_invalid_ones_are_explained()
    {
        Assert.Empty(Branding.Problems(null, " "));
        Assert.Equal(
            ["Het logo moet een volledig adres zijn dat begint met https://.", "De kleur moet een hexcode zijn, bv. #e91e63."],
            Branding.Problems("www.hopsakee.fun/logo.png", "roze"));
    }
}
