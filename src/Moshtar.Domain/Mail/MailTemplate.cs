using System.Text.RegularExpressions;

namespace Moshtar.Domain.Mail;

/// <summary>
/// Onderwerp en tekst van een mailsjabloon in één taal. De tekst bestaat uit alinea's, gescheiden door een
/// lege regel, en kent geen HTML (ADR 0003). Plaatshouders staan tussen accolades, bv. {firstName}.
/// </summary>
public sealed record MailTemplateText(string Subject, string Body);

/// <summary>Een soort mail die een verhuurder aan zijn klanten stuurt.</summary>
public enum MailKind
{
    ReservationConfirmation,
}

/// <summary>
/// Wat een soort mail toelaat: welke plaatshouders, welke ervan verplicht in de tekst, en de standaardtekst
/// van Moshtar per taal. Een blokplaatshouder zoals {reservationDetails} wordt een vast blok en kan niet in het onderwerp.
/// </summary>
public sealed partial class MailKindDefinition(
    MailKind kind,
    IReadOnlyList<string> placeholders,
    IReadOnlyList<string> requiredInBody,
    IReadOnlyList<string> blocks,
    IReadOnlyDictionary<string, MailTemplateText> standardByCulture)
{
    public MailKind Kind { get; } = kind;
    public IReadOnlyList<string> Placeholders { get; } = placeholders;
    public IReadOnlyList<string> Blocks { get; } = blocks;

    /// <summary>De standaardtekst van Moshtar in deze taal, of in het Nederlands als de taal onbekend is.</summary>
    public MailTemplateText Standard(string culture) =>
        standardByCulture.GetValueOrDefault(culture) ?? standardByCulture["nl"];

    /// <summary>Wat er mis is met deze tekst, als zinnen voor de beheerder. Leeg als ze verstuurd kan worden.</summary>
    public IReadOnlyList<string> Problems(MailTemplateText text)
    {
        var problems = new List<string>();
        foreach (var name in PlaceholdersIn(text.Subject).Concat(PlaceholdersIn(text.Body)).Distinct())
            if (!Placeholders.Contains(name))
                problems.Add($"Onbekende plaatshouder {{{name}}}.");
        foreach (var block in Blocks)
            if (PlaceholdersIn(text.Subject).Contains(block))
                problems.Add($"{{{block}}} kan niet in het onderwerp.");
        foreach (var required in requiredInBody)
            if (!PlaceholdersIn(text.Body).Contains(required))
                problems.Add($"{{{required}}} ontbreekt in de tekst.");
        return problems;
    }

    /// <summary>Vervangt elke gekende plaatshouder via <paramref name="value"/>; andere tekst blijft ongemoeid.</summary>
    public static string Fill(string text, Func<string, string?> value) =>
        PlaceholderPattern().Replace(text, m => value(m.Groups[1].Value) ?? m.Value);

    private static IEnumerable<string> PlaceholdersIn(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex PlaceholderPattern();
}

/// <summary>De soorten klantmails met hun plaatshouders en standaardteksten.</summary>
public static class MailKinds
{
    public const string ReservationDetails = "reservationDetails";

    public static readonly MailKindDefinition ReservationConfirmation = new(
        MailKind.ReservationConfirmation,
        placeholders: ["firstName", "lastName", "reservationNumber", "rentalPeriod", "companyName", "contactEmail", "phone", ReservationDetails],
        requiredInBody: [ReservationDetails],
        blocks: [ReservationDetails],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Bevestiging van je reservatie {reservationNumber} bij {companyName}",
                """
                Hallo {firstName},

                Bedankt voor je reservatie bij {companyName}. Ze is bevestigd.

                {reservationDetails}

                Vragen of iets wijzigen? Antwoord gewoon op deze mail.

                {companyName}
                """),
            ["fr"] = new(
                "Confirmation de votre réservation {reservationNumber} chez {companyName}",
                """
                Bonjour {firstName},

                Merci pour votre réservation chez {companyName}. Elle est confirmée.

                {reservationDetails}

                Une question ou un changement ? Répondez simplement à cet e-mail.

                {companyName}
                """),
            ["en"] = new(
                "Confirmation of your reservation {reservationNumber} with {companyName}",
                """
                Hello {firstName},

                Thank you for your reservation with {companyName}. It is confirmed.

                {reservationDetails}

                Questions or changes? Simply reply to this email.

                {companyName}
                """),
        });
}
