using Moshtar.Domain.Catalog;

namespace Moshtar.Domain.Tests;

/// <summary>De slug van een artikel: één in alle talen, uit de naam gemaakt en uniek bij de verhuurder.</summary>
public class RentalItemSlugTests
{
    [Theory]
    [InlineData("Springkasteel Jungle", "springkasteel-jungle")]
    [InlineData("Château Pirate", "chateau-pirate")]
    [InlineData("  Spel: 4 op 'n rij!! ", "spel-4-op-n-rij")]
    [InlineData("🎈", "")]
    public void A_slug_is_made_from_the_name(string name, string expected) =>
        Assert.Equal(expected, RentalItemSlug.From(name));

    [Fact]
    public void A_long_name_gives_a_slug_of_at_most_100_characters_without_a_trailing_hyphen()
    {
        var slug = RentalItemSlug.From(new string('a', 99) + " b");

        Assert.Equal(new string('a', 99), slug);
    }

    [Theory]
    [InlineData("springkasteel-jungle", null)]
    [InlineData("", "De slug is leeg.")]
    [InlineData("Jungle", "De slug \"Jungle\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. springkasteel-jungle.")]
    [InlineData("jungle-", "De slug \"jungle-\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. springkasteel-jungle.")]
    // Een artikel staat onder /huren/: woorden die voor een categorie gereserveerd zijn, mogen hier wel.
    [InlineData("contact", null)]
    public void A_slug_has_lowercase_letters_digits_and_hyphens(string slug, string? problem) =>
        Assert.Equal(problem, RentalItemSlug.Problem(slug));

    [Fact]
    public void A_slug_that_is_taken_gets_a_number()
    {
        Assert.Equal("jungle", RentalItemSlug.Unique("jungle", ["piraat"]));
        Assert.Equal("jungle-2", RentalItemSlug.Unique("jungle", ["jungle"]));
        Assert.Equal("jungle-3", RentalItemSlug.Unique("jungle", ["jungle", "jungle-2"]));
    }
}
