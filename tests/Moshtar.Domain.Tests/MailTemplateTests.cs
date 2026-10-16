using Moshtar.Domain.Mail;

namespace Moshtar.Domain.Tests;

public class MailTemplateTests
{
    private static readonly MailKindDefinition Confirmation = MailKinds.ReservationConfirmation;

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("en")]
    public void Reservation_confirmation_has_a_valid_standard_text_in_every_language(string culture) =>
        Assert.Empty(Confirmation.Problems(Confirmation.Standard(culture)));

    [Theory]
    [InlineData(MailKind.ReservationConfirmation)]
    [InlineData(MailKind.ExpectedDeliveryTime)]
    [InlineData(MailKind.ExpectedCollectionTime)]
    [InlineData(MailKind.ReservationAlternatives)]
    [InlineData(MailKind.ReviewRequest)]
    public void Every_mail_about_a_reservation_knows_the_company_vat_number_and_billing_address_of_the_customer(MailKind kind)
    {
        var definition = MailKinds.For(kind);
        var standard = definition.Standard("nl");

        Assert.Empty(definition.Problems(standard with { Subject = standard.Subject + " {customerCompanyName}", Body = standard.Body + "\n\n{customerVatNumber}, {billingAddress}" }));
    }

    [Fact]
    public void A_cart_reminder_has_no_customer_yet() =>
        Assert.Equal(["Onbekende plaatshouder {customerCompanyName}."],
            MailKinds.CartReminder.Problems(MailKinds.CartReminder.Standard("nl") with { Subject = "{customerCompanyName}" }));

    [Theory]
    [InlineData(MailKind.ExpectedDeliveryTime, "nl")]
    [InlineData(MailKind.ExpectedDeliveryTime, "fr")]
    [InlineData(MailKind.ExpectedDeliveryTime, "en")]
    [InlineData(MailKind.ExpectedCollectionTime, "nl")]
    [InlineData(MailKind.ExpectedCollectionTime, "fr")]
    [InlineData(MailKind.ExpectedCollectionTime, "en")]
    public void Expected_time_mails_have_a_valid_standard_text_with_the_window_in_every_language(MailKind kind, string culture)
    {
        var standard = MailKinds.For(kind).Standard(culture);

        Assert.Empty(MailKinds.For(kind).Problems(standard));
        Assert.Contains("{expectedWindow}", standard.Body);
    }

    [Theory]
    [InlineData(MailKind.ExpectedDeliveryTime, "nl", "Hallo {firstName}")]
    [InlineData(MailKind.ExpectedDeliveryTime, "fr", "Bonjour {firstName}")]
    [InlineData(MailKind.ExpectedDeliveryTime, "en", "Hello {firstName}")]
    [InlineData(MailKind.ExpectedCollectionTime, "nl", "Hallo {firstName}")]
    [InlineData(MailKind.ExpectedCollectionTime, "fr", "Bonjour {firstName}")]
    [InlineData(MailKind.ExpectedCollectionTime, "en", "Hello {firstName}")]
    public void Expected_time_mails_have_a_short_whatsapp_message_with_the_window_in_every_language(MailKind kind, string culture, string greeting)
    {
        var definition = MailKinds.For(kind);
        var whatsApp = definition.Standard(culture).WhatsApp;

        Assert.True(definition.HasWhatsApp);
        Assert.NotNull(whatsApp);
        Assert.StartsWith(greeting, whatsApp);
        Assert.Contains("{expectedWindow}", whatsApp);
        Assert.DoesNotContain("*", whatsApp);
        Assert.Empty(definition.Problems(definition.Standard(culture)));
    }

    [Fact]
    public void Other_mails_have_no_whatsapp_message()
    {
        Assert.False(Confirmation.HasWhatsApp);
        Assert.Null(Confirmation.Standard("nl").WhatsApp);
    }

    [Fact]
    public void The_whatsapp_message_knows_the_same_placeholders_and_needs_the_window()
    {
        var standard = MailKinds.ExpectedCollectionTime.Standard("nl");

        var problems = MailKinds.ExpectedCollectionTime.Problems(standard with { WhatsApp = "Hallo {voornaam}, we komen {expectedDate}." });

        Assert.Equal(["Onbekende plaatshouder {voornaam}.", "{expectedWindow} ontbreekt in het WhatsApp-bericht."], problems);
    }

    [Fact]
    public void The_window_is_required_in_an_expected_time_mail()
    {
        var problems = MailKinds.ExpectedDeliveryTime.Problems(new MailTemplateText("Levering op {expectedDate}", "Hallo {firstName}, tot {expectedDate}."));

        Assert.Equal(["{expectedWindow} ontbreekt in de tekst."], problems);
    }

    [Fact]
    public void Standard_text_of_an_unknown_language_is_dutch() =>
        Assert.Equal(Confirmation.Standard("nl"), Confirmation.Standard("de"));

    [Fact]
    public void Reservation_details_are_required_in_the_reservation_confirmation()
    {
        var problems = Confirmation.Problems(new MailTemplateText("Je reservatie {reservationNumber}", "Hallo {firstName}"));

        Assert.Equal(["{reservationDetails} ontbreekt in de tekst."], problems);
    }

    [Fact]
    public void Unknown_placeholders_are_named_wherever_they_are()
    {
        var problems = Confirmation.Problems(new MailTemplateText("Je reservatie {reservatienummer}", "Hallo {firstNam},\n\n{reservationDetails}"));

        Assert.Equal(["Onbekende plaatshouder {reservatienummer}.", "Onbekende plaatshouder {firstNam}."], problems);
    }

    [Fact]
    public void Reservation_details_are_a_block_and_do_not_belong_in_the_subject()
    {
        var problems = Confirmation.Problems(new MailTemplateText("{reservationDetails}", "{reservationDetails}"));

        Assert.Equal(["{reservationDetails} kan niet in het onderwerp."], problems);
    }

    [Theory]
    [InlineData("{first_name}")]
    [InlineData("{firstName1}")]
    [InlineData("{first-name}")]
    public void Typos_with_other_characters_are_unknown_placeholders(string typo) =>
        Assert.Equal([$"Onbekende plaatshouder {typo}."],
            Confirmation.Problems(new MailTemplateText("Hallo " + typo, "{reservationDetails}")));

    [Fact]
    public void Placeholders_are_case_sensitive() =>
        Assert.Equal(["Onbekende plaatshouder {FirstName}."],
            Confirmation.Problems(new MailTemplateText("Hallo {FirstName}", "{reservationDetails}")));

    [Fact]
    public void Braces_without_a_name_are_ordinary_text() =>
        Assert.Empty(Confirmation.Problems(new MailTemplateText("Tot {} snel :-}", "{reservationDetails} { geen plaatshouder }")));
    [Theory]
    [InlineData("nl", "Hallo {firstName}")]
    [InlineData("fr", "Bonjour {firstName}")]
    [InlineData("en", "Hello {firstName}")]
    public void The_review_request_has_a_neutral_standard_text_with_the_review_link_and_qr_code_in_every_language(string culture, string greeting)
    {
        var definition = MailKinds.For(MailKind.ReviewRequest);
        var standard = definition.Standard(culture);

        Assert.Empty(definition.Problems(standard));
        Assert.StartsWith(greeting, standard.Body);
        Assert.Contains("{reviewLink}", standard.Body);
        Assert.Contains("{reviewQrCode}", standard.Body);
        // Geen beloning of actie: Google verbiedt het.
        foreach (var forbidden in new[] { "korting", "actie", "réduction", "promo", "discount", "%" })
            Assert.DoesNotContain(forbidden, standard.Body + standard.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("fr")]
    [InlineData("en")]
    public void The_review_request_has_a_short_whatsapp_message_with_the_review_link(string culture)
    {
        var definition = MailKinds.ReviewRequest;
        var whatsApp = definition.Standard(culture).WhatsApp;

        Assert.True(definition.HasWhatsApp);
        Assert.Contains("{reviewLink}", whatsApp);
        Assert.DoesNotContain("{reviewQrCode}", whatsApp);
        Assert.DoesNotContain("*", whatsApp);
    }

    [Fact]
    public void The_review_link_is_required_and_the_qr_code_is_optional_in_a_review_request()
    {
        var standard = MailKinds.ReviewRequest.Standard("nl");

        Assert.Equal(["{reviewLink} ontbreekt in de tekst."],
            MailKinds.ReviewRequest.Problems(standard with { Body = "Hallo {firstName}, laat een review achter. {reviewQrCode}" }));
        Assert.Empty(MailKinds.ReviewRequest.Problems(standard with { Body = "Hallo {firstName}, {reviewLink}" }));
        Assert.Equal(["{reviewQrCode} kan niet in het onderwerp."],
            MailKinds.ReviewRequest.Problems(standard with { Subject = "Review {reviewQrCode}" }));
    }
}
