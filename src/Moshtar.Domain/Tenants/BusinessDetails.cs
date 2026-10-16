using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Tenants;

/// <summary>
/// De bedrijfsgegevens van een verhuurder (adres, telefoon, WhatsApp, btw-nummer, sociale media, Google-bedrijfsprofiel)
/// en zijn werkgebied. Links zijn altijd volledige https-adressen en telefoonnummers staan in internationaal formaat;
/// zo komt er nooit iets anders in een link op de reservatiewebsite.
/// </summary>
public static partial class BusinessDetails
{
    /// <summary>Het telefoonnummer zoals ingevuld (bv. "+32 475 12 34 56"), of null als het geen internationaal nummer is.</summary>
    public static string? Phone(string? phone) =>
        phone?.Trim() is { Length: > 0 } p && InternationalNumber().IsMatch(Dialable(p)) ? p : null;

    /// <summary>Het nummer voor een tel:-link: een + en cijfers, bv. "+32475123456".</summary>
    public static string TelLink(string phone) => Dialable(phone);

    /// <summary>De WhatsApp-link naar een (geldig) nummer, bv. "https://wa.me/32475123456".</summary>
    public static string WhatsAppLink(string phone) => "https://wa.me/" + Dialable(phone).TrimStart('+');

    /// <summary>Een link zoals ze gebruikt wordt, of null als ze geen volledig https-adres is.</summary>
    public static string? Link(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0
            ? uri.AbsoluteUri
            : null;

    /// <summary>Het e-mailadres zonder spaties errond, of null als het geen e-mailadres is.</summary>
    public static string? Email(string? email) =>
        email?.Trim() is { Length: > 0 } e && MailAddress.TryCreate(e, out var address) && address.Address == e && e.Contains('.')
            ? e
            : null;

    /// <summary>De gemeenten van het werkgebied, één per regel of gescheiden door komma's; zonder lege of dubbele.</summary>
    public static IReadOnlyList<string> ServiceArea(string? municipalities) =>
        (municipalities ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Wat er mis is met een ingevuld veld. Leeg is in orde, behalve het e-mailadres: klanten antwoorden daarop
    /// op de mails van de verhuurder, dus zonder kan hij niet mailen.
    /// </summary>
    public static IReadOnlyList<string> Problems(
        string? email, string? phone, string? whatsApp,
        string? facebook, string? instagram, string? tikTok, string? googleBusiness)
    {
        var problems = new List<string>();
        if (!Filled(email))
            problems.Add("Het e-mailadres is verplicht: klanten antwoorden erop als ze je mails beantwoorden.");
        else if (Email(email) is null)
            problems.Add("Het e-mailadres is niet geldig.");
        if (Filled(phone) && Phone(phone) is null)
            problems.Add("Het telefoonnummer moet in internationaal formaat staan, bv. +32 475 12 34 56.");
        if (Filled(whatsApp) && Phone(whatsApp) is null)
            problems.Add("Het WhatsApp-nummer moet in internationaal formaat staan, bv. +32 475 12 34 56.");
        foreach (var (name, url) in new[] { ("Facebook", facebook), ("Instagram", instagram), ("TikTok", tikTok), ("Google-bedrijfsprofiel", googleBusiness) })
            if (Filled(url) && Link(url) is null)
                problems.Add($"De link naar je {name} moet een volledig adres zijn dat begint met https://.");
        return problems;
    }

    /// <summary>
    /// Het GA4-meet-ID van de verhuurder, in hoofdletters ("G-ABC123XYZ"), of null als het leeg of ongeldig is.
    /// Het komt letterlijk in het Google-script, dus alleen G- met letters en cijfers.
    /// </summary>
    public static string? GoogleAnalyticsId(string? id) =>
        id?.Trim().ToUpperInvariant() is { } upper && MeasurementId().IsMatch(upper) ? upper : null;

    /// <summary>Wat er mis is met een ingevuld meet-ID, of null als het leeg of goed is.</summary>
    public static string? GoogleAnalyticsProblem(string? id) =>
        Filled(id) && GoogleAnalyticsId(id) is null
            ? "Het meet-ID van Google Analytics begint met G- gevolgd door letters en cijfers, bv. G-ABC123XYZ."
            : null;

    /// <summary>
    /// Wat er mis is met de reviewlink en de schakelaar "Reviewvragen sturen", of null als ze in orde zijn. De link mag
    /// leeg zijn, maar dan kan de schakelaar niet aan: er vertrekt nooit een reviewvraag zonder link.
    /// </summary>
    public static string? ReviewLinkProblem(string? reviewLink, bool sendReviewRequests) =>
        Filled(reviewLink) && Link(reviewLink) is null
            ? "De Google-reviewlink moet een volledig adres zijn dat begint met https://, zoals Google het geeft bij \"Vraag om reviews\"."
            : sendReviewRequests && !Filled(reviewLink)
                ? "Vul eerst je Google-reviewlink in: zonder link kunnen er geen reviewvragen vertrekken."
                : null;

    private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Dialable(string phone) => NotDialable().Replace(phone, "");

    [GeneratedRegex("[\\s./()-]")]
    private static partial Regex NotDialable();

    [GeneratedRegex("^G-[A-Z0-9]{4,20}$")]
    private static partial Regex MeasurementId();

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$")]
    private static partial Regex InternationalNumber();
}
