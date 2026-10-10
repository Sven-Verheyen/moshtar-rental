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

    [Fact]
    public void Placeholders_are_case_sensitive() =>
        Assert.Equal(["Onbekende plaatshouder {FirstName}."],
            Confirmation.Problems(new MailTemplateText("Hallo {FirstName}", "{reservationDetails}")));

    [Fact]
    public void Braces_without_a_name_are_ordinary_text() =>
        Assert.Empty(Confirmation.Problems(new MailTemplateText("Tot {} snel :-}", "{reservationDetails} { geen plaatshouder }")));
}
