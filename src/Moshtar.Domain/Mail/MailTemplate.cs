using System.Text.RegularExpressions;

namespace Moshtar.Domain.Mail;

/// <summary>
/// Onderwerp en tekst van een mailsjabloon in één taal. De tekst bestaat uit alinea's, gescheiden door een
/// lege regel, en kent geen HTML (ADR 0003). Plaatshouders staan tussen accolades, bv. {firstName}.
/// <see cref="WhatsApp"/> is het korte WhatsApp-bericht met dezelfde plaatshouders, zonder opmaak; enkel bij een soort
/// mail die er een heeft (<see cref="MailKindDefinition.HasWhatsApp"/>), anders null.
/// </summary>
public sealed record MailTemplateText(string Subject, string Body, string? WhatsApp = null);

/// <summary>Een soort mail die een verhuurder aan zijn klanten stuurt.</summary>
public enum MailKind
{
    ReservationConfirmation,
    CartReminder,
    /// <summary>Het verwacht tijdsvenster van een levering, per leverronde met de hand gemaild.</summary>
    ExpectedDeliveryTime,
    /// <summary>Het verwacht tijdsvenster van een ophaling, per ophaalronde met de hand gemaild.</summary>
    ExpectedCollectionTime,
    ReservationAlternatives,
    /// <summary>De reviewvraag, de dag na de einddag van elke reservatie vanzelf gemaild.</summary>
    ReviewRequest,
}

/// <summary>
/// Wat een soort mail toelaat: welke plaatshouders, welke ervan verplicht in de tekst, en de standaardtekst
/// van Moshtar per taal. Een blokplaatshouder zoals {reservationDetails} wordt een vast blok en kan niet in het onderwerp.
/// </summary>
public sealed partial class MailKindDefinition(
    MailKind kind,
    string name,
    IReadOnlyList<string> placeholders,
    IReadOnlyList<string> requiredInBody,
    IReadOnlyList<string> blocks,
    IReadOnlyDictionary<string, MailTemplateText> standardByCulture)
{
    public MailKind Kind { get; } = kind;
    /// <summary>Naam voor de beheerder, bv. "Reservatiebevestiging".</summary>
    public string Name { get; } = name;
    public IReadOnlyList<string> Placeholders { get; } = placeholders;
    public IReadOnlyList<string> Blocks { get; } = blocks;

    /// <summary>Heeft deze soort mail ook een WhatsApp-bericht (Verwacht leveruur, Verwacht ophaaluur en Reviewvraag)?</summary>
    public bool HasWhatsApp => standardByCulture["nl"].WhatsApp is not null;

    /// <summary>Moet deze plaatshouder in de tekst staan?</summary>
    public bool IsRequired(string placeholder) => requiredInBody.Contains(placeholder);

    /// <summary>De standaardtekst van Moshtar in deze taal, of in het Nederlands als de taal onbekend is.</summary>
    public MailTemplateText Standard(string culture) =>
        standardByCulture.GetValueOrDefault(culture) ?? standardByCulture["nl"];

    /// <summary>
    /// De tekst van een aanpassing zoals ze geldt: een aanpassing van voor er WhatsApp-berichten waren, krijgt het
    /// standaardbericht in die taal.
    /// </summary>
    public MailTemplateText WithStandardWhatsApp(MailTemplateText text, string culture) =>
        HasWhatsApp && text.WhatsApp is null ? text with { WhatsApp = Standard(culture).WhatsApp } : text;

    /// <summary>Wat er mis is met deze tekst, als zinnen voor de beheerder. Leeg als ze verstuurd kan worden.</summary>
    public IReadOnlyList<string> Problems(MailTemplateText text)
    {
        var problems = new List<string>();
        var whatsApp = PlaceholdersIn(text.WhatsApp ?? "").ToList();
        foreach (var name in PlaceholdersIn(text.Subject).Concat(PlaceholdersIn(text.Body)).Concat(whatsApp).Distinct())
            if (!Placeholders.Contains(name))
                problems.Add($"Onbekende plaatshouder {{{name}}}.");
        foreach (var block in Blocks)
            if (PlaceholdersIn(text.Subject).Contains(block))
                problems.Add($"{{{block}}} kan niet in het onderwerp.");
        foreach (var url in MailFormatting.ForbiddenLinks(text.Body))
            problems.Add($"De link naar \"{url}\" moet een volledig adres zijn dat begint met https://, http:// of mailto:.");
        foreach (var required in requiredInBody)
            if (!PlaceholdersIn(text.Body).Contains(required))
                problems.Add($"{{{required}}} ontbreekt in de tekst.");
        if (text.WhatsApp is not null)
            foreach (var required in requiredInBody)
                if (!whatsApp.Contains(required))
                    problems.Add($"{{{required}}} ontbreekt in het WhatsApp-bericht.");
        return problems;
    }

    /// <summary>Vervangt elke gekende plaatshouder via <paramref name="value"/>; andere tekst blijft ongemoeid.</summary>
    public static string Fill(string text, Func<string, string?> value) =>
        PlaceholderPattern().Replace(text, m => value(m.Groups[1].Value) ?? m.Value);

    private static IEnumerable<string> PlaceholdersIn(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value);

    // Alles zonder spaties tussen accolades is een plaatshouder, zodat ook een tikfout als {first_name} opvalt.
    [GeneratedRegex(@"\{([^\s{}]+)\}")]
    private static partial Regex PlaceholderPattern();
}

/// <summary>De soorten klantmails met hun plaatshouders en standaardteksten.</summary>
public static class MailKinds
{
    public const string ReservationDetails = "reservationDetails";
    public const string CartContents = "cartContents";
    public const string CartLink = "cartLink";
    /// <summary>Het verwacht tijdsvenster van de stop, bv. 9u30 – 10u30.</summary>
    public const string ExpectedWindow = "expectedWindow";
    /// <summary>De dag van de levering of ophaling, voluit.</summary>
    public const string ExpectedDate = "expectedDate";
    public const string DeliveryAddress = "deliveryAddress";
    /// <summary>De bedrijfsnaam van een zakelijke klant; leeg voor een particuliere klant. {companyName} is die van de verhuurder.</summary>
    public const string CustomerCompanyName = "customerCompanyName";
    /// <summary>Het btw-nummer van de klant; leeg als hij er geen heeft.</summary>
    public const string CustomerVatNumber = "customerVatNumber";
    /// <summary>Het factuuradres van de klant, of zijn adres als het factuuradres hetzelfde is.</summary>
    public const string BillingAddress = "billingAddress";
    public const string Alternatives = "alternatives";
    /// <summary>De knop "Schrijf een review" naar de reviewlink; in het WhatsApp-bericht de link zelf.</summary>
    public const string ReviewLink = "reviewLink";
    /// <summary>De QR-code van de reviewlink, om met een gsm te scannen.</summary>
    public const string ReviewQrCode = "reviewQrCode";

    /// <summary>Wat een blokplaatshouder invoegt, voor de beheerder.</summary>
    public static string Describe(string block) => block switch
    {
        ReservationDetails => "voegt het vaste overzicht van de reservatie in",
        CartContents => "voegt de huurperiode en wat in de winkelkar zit in",
        CartLink => "voegt de link in die de winkelkar opent, ook op een ander toestel",
        ReviewLink => "voegt de knop \"Schrijf een review\" naar je reviewlink in; in het WhatsApp-bericht de link zelf",
        ReviewQrCode => "voegt de QR-code van je reviewlink in, om met een gsm te scannen",
        Alternatives => "voegt de aangevinkte alternatieven in, elk met foto, naam, meerprijs of \"zonder meerprijs\" en een link naar de artikelpagina",
        _ => "voegt een vast blok in",
    };

    public static readonly MailKindDefinition ReservationConfirmation = new(
        MailKind.ReservationConfirmation,
        "Reservatiebevestiging",
        placeholders: ["firstName", "lastName", "reservationNumber", "rentalPeriod", "companyName", "contactEmail", "phone", CustomerCompanyName, CustomerVatNumber, BillingAddress, ReservationDetails],
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

    public static readonly MailKindDefinition CartReminder = new(
        MailKind.CartReminder,
        "Herinnering winkelkar",
        placeholders: ["rentalPeriod", "companyName", "contactEmail", "phone", CartContents, CartLink],
        requiredInBody: [CartLink],
        blocks: [CartContents, CartLink],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Je winkelkar bij {companyName} staat nog klaar",
                """
                Hallo,

                Je legde iets in je winkelkar bij {companyName}, maar je reservatie is nog niet afgerond.

                {cartContents}

                {cartLink}

                Je winkelkar houdt niets voor je vast: pas als je reserveert, ben je zeker. Vragen? Antwoord gewoon op deze mail.

                {companyName}
                """),
            ["fr"] = new(
                "Votre panier chez {companyName} vous attend",
                """
                Bonjour,

                Vous avez ajouté quelque chose à votre panier chez {companyName}, mais votre réservation n'est pas encore finalisée.

                {cartContents}

                {cartLink}

                Votre panier ne bloque rien pour vous : vous n'êtes sûr qu'une fois la réservation faite. Une question ? Répondez simplement à cet e-mail.

                {companyName}
                """),
            ["en"] = new(
                "Your cart at {companyName} is still waiting",
                """
                Hello,

                You put something in your cart at {companyName}, but you have not finished your reservation yet.

                {cartContents}

                {cartLink}

                Your cart does not hold anything for you: only a reservation makes it sure. Questions? Simply reply to this email.

                {companyName}
                """),
        });

    private static readonly string[] ExpectedTimePlaceholders =
        ["firstName", "lastName", "reservationNumber", "rentalPeriod", ExpectedDate, ExpectedWindow, DeliveryAddress, "companyName", "contactEmail", "phone", CustomerCompanyName, CustomerVatNumber, BillingAddress];

    public static readonly MailKindDefinition ExpectedDeliveryTime = new(
        MailKind.ExpectedDeliveryTime,
        "Verwacht leveruur",
        placeholders: ExpectedTimePlaceholders,
        requiredInBody: [ExpectedWindow],
        blocks: [],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Levering van je reservatie bij {companyName} op {expectedDate}: {expectedWindow}",
                """
                Hallo {firstName},

                We leveren je reservatie {reservationNumber} op {expectedDate} op {deliveryAddress}. We verwachten aan te komen in het tijdsvenster van {expectedWindow}.

                Het verkeer kan dat nog wat verschuiven. Zorg je dat er iemand is om ons de plek te tonen?

                Vragen of iets wijzigen? Antwoord gewoon op deze mail.

                {companyName}
                """,
                "Hallo {firstName}, we leveren je reservatie {reservationNumber} bij {companyName} op {expectedDate}. We verwachten aan te komen tussen {expectedWindow}. Tot dan!"),
            ["fr"] = new(
                "Livraison de votre réservation chez {companyName} le {expectedDate} : {expectedWindow}",
                """
                Bonjour {firstName},

                Nous livrons votre réservation {reservationNumber} le {expectedDate} à l'adresse {deliveryAddress}. Nous prévoyons d'arriver dans la plage horaire {expectedWindow}.

                La circulation peut encore la décaler un peu. Pouvez-vous veiller à ce que quelqu'un soit présent pour nous montrer l'emplacement ?

                Une question ou un changement ? Répondez simplement à cet e-mail.

                {companyName}
                """,
                "Bonjour {firstName}, nous livrons votre réservation {reservationNumber} chez {companyName} le {expectedDate}. Nous prévoyons d'arriver entre {expectedWindow}. À bientôt !"),
            ["en"] = new(
                "Delivery of your reservation with {companyName} on {expectedDate}: {expectedWindow}",
                """
                Hello {firstName},

                We deliver your reservation {reservationNumber} on {expectedDate} at {deliveryAddress}. We expect to arrive within the time window {expectedWindow}.

                Traffic may still shift it a little. Could you make sure someone is there to show us where to set up?

                Questions or changes? Simply reply to this email.

                {companyName}
                """,
                "Hello {firstName}, we deliver your reservation {reservationNumber} with {companyName} on {expectedDate}. We expect to arrive between {expectedWindow}. See you then!"),
        });

    public static readonly MailKindDefinition ExpectedCollectionTime = new(
        MailKind.ExpectedCollectionTime,
        "Verwacht ophaaluur",
        placeholders: ExpectedTimePlaceholders,
        requiredInBody: [ExpectedWindow],
        blocks: [],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Ophaling van je reservatie bij {companyName} op {expectedDate}: {expectedWindow}",
                """
                Hallo {firstName},

                We komen je reservatie {reservationNumber} ophalen op {expectedDate} op {deliveryAddress}. We verwachten aan te komen in het tijdsvenster van {expectedWindow}.

                Het verkeer kan dat nog wat verschuiven. Zorg je dat alles klaarstaat en dat er iemand is?

                Vragen? Antwoord gewoon op deze mail.

                {companyName}
                """,
                "Hallo {firstName}, we komen je reservatie {reservationNumber} bij {companyName} ophalen op {expectedDate}. We verwachten aan te komen tussen {expectedWindow}. Staat alles klaar?"),
            ["fr"] = new(
                "Reprise de votre réservation chez {companyName} le {expectedDate} : {expectedWindow}",
                """
                Bonjour {firstName},

                Nous venons reprendre votre réservation {reservationNumber} le {expectedDate} à l'adresse {deliveryAddress}. Nous prévoyons d'arriver dans la plage horaire {expectedWindow}.

                La circulation peut encore la décaler un peu. Pouvez-vous veiller à ce que tout soit prêt et que quelqu'un soit présent ?

                Une question ? Répondez simplement à cet e-mail.

                {companyName}
                """,
                "Bonjour {firstName}, nous venons reprendre votre réservation {reservationNumber} chez {companyName} le {expectedDate}. Nous prévoyons d'arriver entre {expectedWindow}. Tout sera prêt ?"),
            ["en"] = new(
                "Collection of your reservation with {companyName} on {expectedDate}: {expectedWindow}",
                """
                Hello {firstName},

                We come to collect your reservation {reservationNumber} on {expectedDate} at {deliveryAddress}. We expect to arrive within the time window {expectedWindow}.

                Traffic may still shift it a little. Could you make sure everything is ready and someone is there?

                Questions? Simply reply to this email.

                {companyName}
                """,
                "Hello {firstName}, we come to collect your reservation {reservationNumber} with {companyName} on {expectedDate}. We expect to arrive between {expectedWindow}. Will everything be ready?"),
        });

    public static readonly MailKindDefinition ReservationAlternatives = new(
        MailKind.ReservationAlternatives,
        "Alternatieven bij een tekort",
        placeholders: ["firstName", "lastName", "reservationNumber", "rentalPeriod", "companyName", "contactEmail", "phone", CustomerCompanyName, CustomerVatNumber, BillingAddress, "unavailableItem", ReservationDetails, Alternatives],
        requiredInBody: [Alternatives],
        blocks: [ReservationDetails, Alternatives],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Een alternatief voor {unavailableItem} in je reservatie {reservationNumber}",
                """
                Hallo {firstName},

                Helaas is {unavailableItem} niet beschikbaar in je huurperiode ({rentalPeriod}). Het spijt ons. Deze artikelen zijn wel vrij:

                {alternatives}

                Laat je ons weten welk alternatief je kiest? Antwoord gewoon op deze mail of bel ons.

                {companyName}
                """),
            ["fr"] = new(
                "Une alternative pour {unavailableItem} dans votre réservation {reservationNumber}",
                """
                Bonjour {firstName},

                Malheureusement, {unavailableItem} n'est pas disponible pendant votre période de location ({rentalPeriod}). Nous en sommes désolés. Ces articles sont bien libres :

                {alternatives}

                Pouvez-vous nous dire quelle alternative vous choisissez ? Répondez simplement à cet e-mail ou appelez-nous.

                {companyName}
                """),
            ["en"] = new(
                "An alternative for {unavailableItem} in your reservation {reservationNumber}",
                """
                Hello {firstName},

                Unfortunately, {unavailableItem} is not available during your rental period ({rentalPeriod}). We are sorry. These items are available:

                {alternatives}

                Could you let us know which alternative you choose? Simply reply to this email or call us.

                {companyName}
                """),
        });

    /// <summary>
    /// De reviewvraag: neutraal, voor elke klant dezelfde, zonder beloning of actie (Google verbiedt beide). De QR-code
    /// is optioneel; in het WhatsApp-bericht hoort ze niet, de link wel.
    /// </summary>
    public static readonly MailKindDefinition ReviewRequest = new(
        MailKind.ReviewRequest,
        "Reviewvraag",
        placeholders: ["firstName", "lastName", "reservationNumber", "rentalPeriod", "companyName", "contactEmail", "phone", CustomerCompanyName, CustomerVatNumber, BillingAddress, ReviewLink, ReviewQrCode],
        requiredInBody: [ReviewLink],
        blocks: [ReviewLink, ReviewQrCode],
        standardByCulture: new Dictionary<string, MailTemplateText>
        {
            ["nl"] = new(
                "Hoe was je huur bij {companyName}?",
                """
                Hallo {firstName},

                Bedankt dat je bij {companyName} huurde. We hopen dat alles naar wens was.

                Wil je even een review schrijven over je ervaring? Zo help je andere klanten kiezen. Het duurt maar een minuutje.

                {reviewLink}

                {reviewQrCode}

                Bedankt!

                {companyName}
                """,
                "Hallo {firstName}, bedankt dat je bij {companyName} huurde! Wil je even een review schrijven over je ervaring? Dat kan hier: {reviewLink}"),
            ["fr"] = new(
                "Comment s'est passée votre location chez {companyName} ?",
                """
                Bonjour {firstName},

                Merci d'avoir loué chez {companyName}. Nous espérons que tout s'est bien passé.

                Pourriez-vous écrire un avis sur votre expérience ? Cela aide d'autres clients à choisir. Cela ne prend qu'une minute.

                {reviewLink}

                {reviewQrCode}

                Merci !

                {companyName}
                """,
                "Bonjour {firstName}, merci d'avoir loué chez {companyName} ! Pourriez-vous écrire un avis sur votre expérience ? C'est ici : {reviewLink}"),
            ["en"] = new(
                "How was your rental with {companyName}?",
                """
                Hello {firstName},

                Thank you for renting with {companyName}. We hope everything went well.

                Would you take a moment to write a review about your experience? It helps other customers choose. It only takes a minute.

                {reviewLink}

                {reviewQrCode}

                Thank you!

                {companyName}
                """,
                "Hello {firstName}, thank you for renting with {companyName}! Would you take a moment to write a review about your experience? You can do so here: {reviewLink}"),
        });

    public static IReadOnlyList<MailKindDefinition> All { get; } = [ReservationConfirmation, CartReminder, ReservationAlternatives, ExpectedDeliveryTime, ExpectedCollectionTime, ReviewRequest];

    public static MailKindDefinition For(MailKind kind) => All.Single(d => d.Kind == kind);
}
