using Moshtar.Domain.Tenants;

namespace Moshtar.Domain.Tests;

public class BrandingTests
{
    [Theory]
    [InlineData("#1b5e20", "#ffffff")]
    [InlineData("#ff0", "#000000")]
    [InlineData("#0d47a1", "#ffffff")]
    public void Text_on_a_colour_is_black_or_white_whichever_contrasts_most(string color, string expected) =>
        Assert.Equal(expected, Branding.TextColorOn(color));

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
    public void An_empty_colour_is_fine_and_an_invalid_one_is_explained()
    {
        Assert.Empty(Branding.Problems(" "));
        Assert.Equal(["De kleur moet een hexcode zijn, bv. #e91e63."], Branding.Problems("roze"));
    }
}
