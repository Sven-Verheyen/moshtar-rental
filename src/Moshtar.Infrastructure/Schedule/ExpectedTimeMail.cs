using Moshtar.Application.Mail;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Schedule;

/// <summary>
/// De mail met het verwacht tijdsvenster van een levering of ophaling, in de taal van de reservatie, opgebouwd uit het
/// mailsjabloon Verwacht leveruur of Verwacht ophaaluur. Naast de gewone reservatieplaatshouders kent hij
/// {expectedDate}, {expectedWindow} en {deliveryAddress}. Het sjabloon heeft ook een kort WhatsApp-bericht,
/// dat de gebruiker zelf verstuurt via de WhatsApp-knop van de stop.
/// </summary>
internal static class ExpectedTimeMail
{
    /// <summary>Het mailsjabloon voor deze soort ronde.</summary>
    public static MailKind KindFor(RoundKind round) =>
        round == RoundKind.Delivery ? MailKind.ExpectedDeliveryTime : MailKind.ExpectedCollectionTime;

    /// <summary>Het sjabloon in deze taal: de aanpassing van de verhuurder, anders de standaardtekst.</summary>
    public static MailTemplateText Template(IEnumerable<MailTemplateCustomization> customizations, MailKind kind, string language) =>
        customizations.SingleOrDefault(c => c.Kind == kind && c.Culture == language) is { } c
            ? MailKinds.For(kind).WithStandardWhatsApp(c.Text, language)
            : MailKinds.For(kind).Standard(language);

    /// <summary>
    /// De mail voor een fictieve stop in deze taal (venster 9u30 – 10u30), voor het voorbeeld en de testmail van het
    /// mailsjabloon. De reservatie wordt nooit bewaard.
    /// </summary>
    public static MailMessage Example(MailKind kind, string language, DateOnly today, Tenant tenant, string? logoUrl, MailTemplateText template)
    {
        var reservation = ReservationConfirmation.Example(language, today);
        var day = kind == MailKind.ExpectedCollectionTime ? reservation.EndDate : reservation.StartDate;
        return Create(reservation, tenant, logoUrl, template, day, new ExpectedWindow(new TimeOnly(9, 30), new TimeOnly(10, 30)));
    }

    /// <summary>Het WhatsApp-bericht voor een fictieve stop in deze taal (venster 9u30 – 10u30), zoals <see cref="Example"/>.</summary>
    public static string ExampleWhatsApp(MailKind kind, string language, DateOnly today, Tenant tenant, MailTemplateText template)
    {
        var reservation = ReservationConfirmation.Example(language, today);
        var day = kind == MailKind.ExpectedCollectionTime ? reservation.EndDate : reservation.StartDate;
        return WhatsApp(reservation, tenant, template, day, new ExpectedWindow(new TimeOnly(9, 30), new TimeOnly(10, 30)));
    }

    /// <param name="day">De dag van de levering of ophaling.</param>
    /// <param name="logoUrl">Het publieke adres van het logo van de verhuurder, of null.</param>
    /// <param name="template">Het mailsjabloon in de taal van <see cref="ReservationConfirmation.LanguageOf"/>.</param>
    public static MailMessage Create(Reservation reservation, Tenant tenant, string? logoUrl, MailTemplateText template, DateOnly day, ExpectedWindow window)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var value = Values(reservation, tenant, day, window);
        var (html, text) = MailBody.Compose(tenant, logoUrl, template.Body, value, new Dictionary<string, MailBlock>());
        return new MailMessage(customer.Email, MailKindDefinition.Fill(template.Subject, value), html, text);
    }

    /// <summary>
    /// Het WhatsApp-bericht van het sjabloon, met dezelfde plaatshouders ingevuld als <see cref="Create"/>. Zonder
    /// opmaak: WhatsApp toont de tekst zoals hij is.
    /// </summary>
    public static string WhatsApp(Reservation reservation, Tenant tenant, MailTemplateText template, DateOnly day, ExpectedWindow window) =>
        MailKindDefinition.Fill(template.WhatsApp ?? "", Values(reservation, tenant, day, window));

    /// <summary>
    /// De click-to-chat-link (wa.me) naar het internationale nummer van de klant met het ingevulde WhatsApp-bericht,
    /// of null als de klant geen geldig telefoonnummer heeft.
    /// </summary>
    public static string? WhatsAppLink(Reservation reservation, Tenant tenant, MailTemplateText template, DateOnly day, ExpectedWindow window) =>
        PhoneNumber.International(reservation.Customer?.Phone) is { } phone
            ? $"{BusinessDetails.WhatsAppLink(phone)}?text={Uri.EscapeDataString(WhatsApp(reservation, tenant, template, day, window))}"
            : null;

    private static Func<string, string?> Values(Reservation reservation, Tenant tenant, DateOnly day, ExpectedWindow window)
    {
        var language = ReservationConfirmation.LanguageOf(reservation, tenant);
        var culture = ReservationConfirmation.CultureOf(language);
        var values = ReservationConfirmation.Values(reservation, tenant);
        values[MailKinds.ExpectedDate] = day.ToString("dddd d MMMM yyyy", culture);
        values[MailKinds.ExpectedWindow] = window.ToString(language);
        values[MailKinds.DeliveryAddress] = reservation.DeliveryAddress is { } a ? $"{a.Street}, {a.PostalCode} {a.City}" : "";
        return name => values.GetValueOrDefault(name);
    }
}
