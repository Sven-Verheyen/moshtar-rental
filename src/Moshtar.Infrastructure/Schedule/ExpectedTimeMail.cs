using Moshtar.Application.Mail;
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
/// {expectedDate}, {expectedWindow} en {deliveryAddress}.
/// </summary>
internal static class ExpectedTimeMail
{
    /// <summary>Het mailsjabloon voor deze soort ronde.</summary>
    public static MailKind KindFor(RoundKind round) =>
        round == RoundKind.Delivery ? MailKind.ExpectedDeliveryTime : MailKind.ExpectedCollectionTime;

    /// <summary>
    /// De mail voor een fictieve stop in deze taal (venster 9u30 – 10u30), voor het voorbeeld en de testmail van het
    /// mailsjabloon. De reservatie wordt nooit bewaard.
    /// </summary>
    public static MailMessage Example(MailKind kind, string language, DateOnly today, Tenant tenant, MailTemplateText template)
    {
        var reservation = ReservationConfirmation.Example(language, today);
        var day = kind == MailKind.ExpectedCollectionTime ? reservation.EndDate : reservation.StartDate;
        return Create(reservation, tenant, template, day, new ExpectedWindow(new TimeOnly(9, 30), new TimeOnly(10, 30)));
    }

    /// <param name="day">De dag van de levering of ophaling.</param>
    /// <param name="template">Het mailsjabloon in de taal van <see cref="ReservationConfirmation.LanguageOf"/>.</param>
    public static MailMessage Create(Reservation reservation, Tenant tenant, MailTemplateText template, DateOnly day, ExpectedWindow window)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var language = ReservationConfirmation.LanguageOf(reservation, tenant);
        var culture = ReservationConfirmation.CultureOf(language);
        var values = ReservationConfirmation.Values(reservation, tenant, culture);
        values[MailKinds.ExpectedDate] = day.ToString("dddd d MMMM yyyy", culture);
        values[MailKinds.ExpectedWindow] = window.ToString(language);
        values[MailKinds.DeliveryAddress] = reservation.DeliveryAddress is { } a ? $"{a.Street}, {a.PostalCode} {a.City}" : "";
        string? Value(string name) => values.GetValueOrDefault(name);

        var (html, text) = MailBody.Compose(tenant, template.Body, Value, new Dictionary<string, MailBlock>());
        return new MailMessage(customer.Email, MailKindDefinition.Fill(template.Subject, Value), html, text);
    }
}
