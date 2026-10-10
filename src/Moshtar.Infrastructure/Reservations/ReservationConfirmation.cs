using System.Globalization;
using System.Net;
using System.Text;
using Moshtar.Application.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>De bevestigingsmail die een klant krijgt na een geslaagde reservatie, in zijn eigen taal.</summary>
internal static class ReservationConfirmation
{
    private sealed record Texts(
        string CultureName,
        string Subject,
        string Greeting,
        string Intro,
        string Number,
        string Period,
        string Contents,
        string Total,
        string DeliveryAt,
        string Pickup,
        string Notes,
        string Closing);

    private static readonly Dictionary<string, Texts> ByCulture = new()
    {
        ["nl"] = new("nl-BE",
            "Bevestiging van je reservatie {0} bij {1}",
            "Hallo {0},",
            "Bedankt voor je reservatie bij {0}. Ze is bevestigd.",
            "Reservatienummer",
            "Huurperiode",
            "Wat je huurt",
            "Totaal",
            "Levering op {0}",
            "Afhalen",
            "Opmerkingen",
            "Vragen of iets wijzigen? Antwoord gewoon op deze mail."),
        ["fr"] = new("fr-BE",
            "Confirmation de votre réservation {0} chez {1}",
            "Bonjour {0},",
            "Merci pour votre réservation chez {0}. Elle est confirmée.",
            "Numéro de réservation",
            "Période de location",
            "Votre location",
            "Total",
            "Livraison à {0}",
            "Retrait sur place",
            "Remarques",
            "Une question ou un changement ? Répondez simplement à cet e-mail."),
        ["en"] = new("en-BE",
            "Confirmation of your reservation {0} with {1}",
            "Hello {0},",
            "Thank you for your reservation with {0}. It is confirmed.",
            "Reservation number",
            "Rental period",
            "What you are renting",
            "Total",
            "Delivery to {0}",
            "Pickup",
            "Notes",
            "Questions or changes? Simply reply to this email."),
    };

    public static MailMessage Create(Reservation reservation, Tenant tenant)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var t = ByCulture.GetValueOrDefault(reservation.Culture) ?? ByCulture.GetValueOrDefault(tenant.DefaultCulture) ?? ByCulture["nl"];
        var culture = CultureInfo.GetCultureInfo(t.CultureName);

        var period = reservation.StartDate == reservation.EndDate
            ? Date(reservation.StartDate)
            : $"{Date(reservation.StartDate)} – {Date(reservation.EndDate)}";
        var lines = reservation.Lines.Select(l => $"{l.Quantity} × {l.Description} – {Money(l.LineTotal)}").ToList();
        var delivery = reservation.DeliveryMethod == DeliveryMethod.Delivery && reservation.DeliveryAddress is { } a
            ? string.Format(t.DeliveryAt, $"{a.Street}, {a.PostalCode} {a.City}")
            : t.Pickup;

        var facts = new List<(string Label, string Value)>
        {
            (t.Number, reservation.Number),
            (t.Period, period),
        };

        var text = new StringBuilder()
            .AppendLine(string.Format(t.Greeting, customer.FirstName)).AppendLine()
            .AppendLine(string.Format(t.Intro, tenant.Name)).AppendLine();
        foreach (var (label, value) in facts) text.AppendLine($"{label}: {value}");
        text.AppendLine().AppendLine($"{t.Contents}:");
        foreach (var line in lines) text.AppendLine($"- {line}");
        text.AppendLine($"{t.Total}: {Money(reservation.TotalPrice)}").AppendLine()
            .AppendLine(delivery);
        if (!string.IsNullOrWhiteSpace(reservation.Notes)) text.AppendLine($"{t.Notes}: {reservation.Notes}");
        text.AppendLine().AppendLine(t.Closing).AppendLine().Append(tenant.Name);

        var html = new StringBuilder()
            .Append($"<p>{E(string.Format(t.Greeting, customer.FirstName))}</p>")
            .Append($"<p>{E(string.Format(t.Intro, tenant.Name))}</p><p>");
        foreach (var (label, value) in facts) html.Append($"<strong>{E(label)}:</strong> {E(value)}<br>");
        html.Append($"</p><p><strong>{E(t.Contents)}:</strong></p><ul>");
        foreach (var line in lines) html.Append($"<li>{E(line)}</li>");
        html.Append($"</ul><p><strong>{E(t.Total)}: {E(Money(reservation.TotalPrice))}</strong></p>")
            .Append($"<p>{E(delivery)}</p>");
        if (!string.IsNullOrWhiteSpace(reservation.Notes)) html.Append($"<p><strong>{E(t.Notes)}:</strong> {E(reservation.Notes)}</p>");
        html.Append($"<p>{E(t.Closing)}</p><p>{E(tenant.Name)}</p>");

        return new MailMessage(customer.Email, string.Format(t.Subject, reservation.Number, tenant.Name), html.ToString(), text.ToString());

        string Date(DateOnly d) => d.ToString("d MMMM yyyy", culture);
        string Money(decimal amount) => amount.ToString("C", culture);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
