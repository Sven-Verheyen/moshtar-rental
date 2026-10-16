using System.Net;
using Moshtar.Application.Mail;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Reviews;

/// <summary>
/// De reviewvraag, in de taal van de reservatie, opgebouwd uit het mailsjabloon Reviewvraag. Naast de gewone
/// reservatieplaatshouders kent hij {reviewLink} (de knop "Schrijf een review") en {reviewQrCode} (de QR-code als
/// afbeelding op het hoofddomein van de verhuurder). Het sjabloon heeft ook een kort WhatsApp-bericht met de link zelf.
/// </summary>
internal static class ReviewRequestMail
{
    private sealed record Texts(string Button, string ScanQrCode, string QrCodeAlt, string OptOut);

    private static readonly Dictionary<string, Texts> ByLanguage = new()
    {
        ["nl"] = new("Schrijf een review", "Of scan deze QR-code met je gsm:", "QR-code om een review te schrijven", "Ik wil geen reviewvragen meer"),
        ["fr"] = new("Écrire un avis", "Ou scannez ce code QR avec votre téléphone :", "Code QR pour écrire un avis", "Je ne veux plus de demandes d'avis"),
        ["en"] = new("Write a review", "Or scan this QR code with your phone:", "QR code to write a review", "I don't want review requests anymore"),
    };

    /// <summary>Het sjabloon in deze taal: de aanpassing van de verhuurder, anders de standaardtekst.</summary>
    public static MailTemplateText Template(MailTemplateCustomization? customization, string language) =>
        customization is not null
            ? MailKinds.ReviewRequest.WithStandardWhatsApp(customization.Text, language)
            : MailKinds.ReviewRequest.Standard(language);

    /// <summary>Het volledige adres van de QR-code van de reviewlink op dit domein.</summary>
    public static string QrCodeUrl(string host) => $"https://{host}{ReviewQrCodePath.Path}";

    /// <param name="reviewLink">De reviewlink van de verhuurder (een volledig https-adres).</param>
    /// <param name="qrCodeUrl">Het publieke adres van de QR-code van die link, zie <see cref="QrCodeUrl"/>.</param>
    /// <param name="optOutUrl">De afmeldlink voor dit e-mailadres, zie <see cref="IReviewOptOutLinks"/>; de mail zet hem zelf onderaan.</param>
    /// <param name="logoUrl">Het publieke adres van het logo van de verhuurder, of null.</param>
    /// <param name="template">Het mailsjabloon in de taal van <see cref="ReservationConfirmation.LanguageOf"/>.</param>
    public static MailMessage Create(Reservation reservation, Tenant tenant, string? logoUrl, MailTemplateText template, string reviewLink, string qrCodeUrl,
        string optOutUrl)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var t = ByLanguage[ReservationConfirmation.LanguageOf(reservation, tenant)];
        var values = ReservationConfirmation.Values(reservation, tenant);
        var button = $"<p><a href=\"{E(reviewLink)}\" style=\"display:inline-block;padding:12px 20px;background-color:#1f2937;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold\">{E(t.Button)}</a></p>";
        var qrCode = $"<p>{E(t.ScanQrCode)}<br><img src=\"{E(qrCodeUrl)}\" alt=\"{E(t.QrCodeAlt)}\" width=\"180\" height=\"180\" style=\"display:block;border:0;margin-top:8px\"></p>";

        var (html, text) = MailBody.Compose(tenant, logoUrl, template.Body, name => values.GetValueOrDefault(name),
            new Dictionary<string, MailBlock>
            {
                [MailKinds.ReviewLink] = new(button, $"{t.Button}: {reviewLink}"),
                // Een QR-code scant niemand uit de tekstversie: die heeft de link al.
                [MailKinds.ReviewQrCode] = new(qrCode, ""),
            });
        // De afmeldlink staat altijd onderaan, ook als de verhuurder de tekst aanpaste.
        html += $"<p style=\"font-size:12px;color:#6b7280\"><a href=\"{E(optOutUrl)}\" style=\"color:#6b7280\">{E(t.OptOut)}</a></p>";
        text += $"{Environment.NewLine}{Environment.NewLine}{t.OptOut}: {optOutUrl}";
        return new MailMessage(customer.Email, MailKindDefinition.Fill(template.Subject, name => values.GetValueOrDefault(name)), html, text);
    }

    /// <summary>
    /// Het WhatsApp-bericht van het sjabloon, met dezelfde plaatshouders als <see cref="Create"/> en de reviewlink zelf
    /// voor {reviewLink}. Zonder opmaak: WhatsApp toont de tekst zoals hij is.
    /// </summary>
    public static string WhatsApp(Reservation reservation, Tenant tenant, MailTemplateText template, string reviewLink)
    {
        var values = ReservationConfirmation.Values(reservation, tenant);
        values[MailKinds.ReviewLink] = reviewLink;
        values[MailKinds.ReviewQrCode] = "";
        return MailKindDefinition.Fill(template.WhatsApp ?? "", name => values.GetValueOrDefault(name)).Trim();
    }

    /// <summary>De reviewlink voor het voorbeeld en de testmail als de verhuurder er (nog) geen heeft.</summary>
    public static string ExampleLink(Tenant tenant) =>
        BusinessDetails.Link(tenant.ReviewLink) ?? "https://g.page/r/voorbeeld/review";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
