using Moshtar.Domain.Website;

namespace Moshtar.Domain.Tests;

public class WebsiteTextStandardTests
{
    [Theory]
    [InlineData("nl", new string[0], null)]
    [InlineData("nl", new[] { "Mortsel" }, "Mortsel")]
    [InlineData("nl", new[] { "Mortsel", "Edegem" }, "Mortsel en Edegem")]
    [InlineData("fr", new[] { "Anvers", "Mortsel", "Edegem" }, "Anvers, Mortsel et Edegem")]
    [InlineData("nl", new[] { "Antwerpen", "Mortsel", "Edegem", "Kontich" }, "Antwerpen, Mortsel, Edegem en omstreken")]
    [InlineData("fr", new[] { "Anvers", "Mortsel", "Edegem", "Kontich" }, "Anvers, Mortsel, Edegem et environs")]
    [InlineData("en", new[] { "Antwerp", "Mortsel", "Edegem", "Kontich" }, "Antwerp, Mortsel, Edegem and the surrounding area")]
    public void The_service_area_reads_as_part_of_a_sentence(string culture, string[] municipalities, string? expected) =>
        Assert.Equal(expected, WebsiteTextStandard.Area(culture, municipalities));

    [Fact]
    public void Standard_texts_fill_in_the_name_and_the_service_area()
    {
        Assert.Equal("Springkastelen en feestspellen huren bij Hopsakee.fun", WebsiteTextStandard.HomeTitle("nl", "Hopsakee.fun"));
        Assert.Equal("Reserveer online, wij leveren en zetten alles op in Mortsel en Edegem.", WebsiteTextStandard.HomeSubtitle("nl", ["Mortsel", "Edegem"]));
        Assert.Equal("Reserve online, we deliver and set everything up.", WebsiteTextStandard.HomeSubtitle("en", []));
        Assert.Contains("Mortsel", WebsiteTextStandard.Faq("fr", ["Mortsel"])[0].Answer);
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("en")]
    public void The_standard_faq_covers_delivery_rain_power_cancelling_and_setup(string culture)
    {
        var faq = WebsiteTextStandard.Faq(culture, ["Mortsel"]);

        Assert.Equal(5, faq.Count);
        Assert.All(faq, e => Assert.False(string.IsNullOrWhiteSpace(e.Question) || string.IsNullOrWhiteSpace(e.Answer)));
    }

    [Fact]
    public void An_unknown_language_gets_the_dutch_texts() =>
        Assert.Equal(WebsiteTextStandard.HomeTitle("nl", "X"), WebsiteTextStandard.HomeTitle("de", "X"));

    [Theory]
    [InlineData("Springkastelen", "springkastelen")]
    [InlineData("Châteaux gonflables", "chateaux-gonflables")]
    [InlineData("  Spellen & quiz!! ", "spellen-quiz")]
    [InlineData("🎈", "")]
    public void A_slug_is_made_from_a_name(string name, string expected) =>
        Assert.Equal(expected, CategorySlug.From(name));

    [Theory]
    [InlineData("springkastelen", null)]
    [InlineData("", "De slug is leeg.")]
    [InlineData("Springkastelen", "De slug \"Springkastelen\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. springkastelen.")]
    [InlineData("spring--kastelen", "De slug \"spring--kastelen\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. springkastelen.")]
    [InlineData("huren", "\"huren\" heeft al een andere betekenis op je reservatiewebsite; kies een andere slug.")]
    [InlineData("admin", "\"admin\" heeft al een andere betekenis op je reservatiewebsite; kies een andere slug.")]
    public void A_slug_has_lowercase_letters_digits_and_hyphens_and_no_reserved_word(string slug, string? problem) =>
        Assert.Equal(problem, CategorySlug.Problem(slug));
}
