using Moshtar.Domain.Mail;

namespace Moshtar.Domain.Tests;

public class MailFormattingTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["firstName"] = "Jan <b>",
        ["contactEmail"] = "info@hopsakee.fun",
        ["star"] = "*geen opmaak*",
    };

    private static (string Html, string Text) Render(string paragraph) =>
        MailFormatting.Render(paragraph, name => Values.GetValueOrDefault(name));

    [Fact]
    public void Plain_text_stays_plain_and_is_escaped() =>
        Assert.Equal(("Hallo Jan &lt;b&gt; &amp; co", "Hallo Jan <b> & co"), Render("Hallo {firstName} & co"));

    [Fact]
    public void Bold_and_italic_become_tags_in_html_and_disappear_in_text() =>
        Assert.Equal(("Dit is <strong>vet</strong> en <em>schuin</em>", "Dit is vet en schuin"), Render("Dit is **vet** en *schuin*"));

    [Fact]
    public void A_link_shows_its_address_in_the_text_version() =>
        Assert.Equal(
            ("Zie <a href=\"https://hopsakee.fun/faq\">onze vragen</a>", "Zie onze vragen (https://hopsakee.fun/faq)"),
            Render("Zie [onze vragen](https://hopsakee.fun/faq)"));

    [Fact]
    public void Placeholders_are_filled_inside_formatting_and_links() =>
        Assert.Equal(
            ("<strong>Jan &lt;b&gt;</strong>, mail <a href=\"mailto:info@hopsakee.fun\">ons</a>", "Jan <b>, mail ons (mailto:info@hopsakee.fun)"),
            Render("**{firstName}**, mail [ons](mailto:{contactEmail})"));

    [Fact]
    public void Formatting_in_a_value_typed_by_a_customer_is_never_applied() =>
        Assert.Equal(("Naam: *geen opmaak*", "Naam: *geen opmaak*"), Render("Naam: {star}"));

    [Fact]
    public void A_link_with_another_kind_of_address_stays_text() =>
        Assert.Equal(("[klik](javascript:alert(1))", "[klik](javascript:alert(1))"), Render("[klik](javascript:alert(1))"));

    [Fact]
    public void A_line_break_within_a_paragraph_stays_a_line_break() =>
        Assert.Equal(("Groeten,<br><strong>Hopsakee</strong>", "Groeten,\nHopsakee"), Render("Groeten,\n**Hopsakee**"));

    [Theory]
    [InlineData("[klik](javascript:void)")]
    [InlineData("[klik](www.hopsakee.fun)")]
    [InlineData("[klik](https://)")]
    [InlineData("[klik]({contactEmail})")]
    public void Links_must_start_with_https_http_or_mailto(string link) =>
        Assert.Equal(["De link naar \"" + link[(link.IndexOf('(') + 1)..^1] + "\" moet een volledig adres zijn dat begint met https://, http:// of mailto:."],
            MailKinds.ReservationConfirmation.Problems(new MailTemplateText("Hallo", link + "\n\n{reservationDetails}")));
}
