using System.Globalization;
using System.Net;
using System.Text;
using Moshtar.Application.Mail;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>
/// De bevestigingsmail die een klant krijgt na een geslaagde reservatie, in zijn eigen taal, opgebouwd uit
/// het mailsjabloon. Het overzicht van de reservatie ({reservationDetails}) is een vast blok dat hier gemaakt wordt.
/// </summary>
internal static class ReservationConfirmation
{
    private sealed record DetailTexts(
        string CultureName,
        string Number,
        string Period,
        string Contents,
        string Total,
        string DeliveryAt,
        string Pickup,
        string Notes);

    private static readonly Dictionary<string, DetailTexts> ByCulture = new()
    {
        ["nl"] = new("nl-BE", "Reservatienummer", "Huurperiode", "Wat je huurt", "Totaal", "Levering op {0}", "Afhalen", "Opmerkingen"),
        ["fr"] = new("fr-BE", "Numéro de réservation", "Période de location", "Votre location", "Total", "Livraison à {0}", "Retrait sur place", "Remarques"),
        ["en"] = new("en-BE", "Reservation number", "Rental period", "What you are renting", "Total", "Delivery to {0}", "Pickup", "Notes"),
    };

    private sealed record ExampleTexts(string FirstName, string LastName, string Item, string Notes);

    private static readonly Dictionary<string, ExampleTexts> Examples = new()
    {
        ["nl"] = new("Lotte", "Peeters", "Springkasteel Piraat", "Graag levering voor 10 uur."),
        ["fr"] = new("Camille", "Dubois", "Château gonflable Pirate", "Livraison avant 10 heures, s'il vous plaît."),
        ["en"] = new("Alex", "Smith", "Bouncy castle Pirate", "Please deliver before 10 am."),
    };

    /// <summary>Een fictieve reservatie in deze taal, voor het voorbeeld en de testmail van een mailsjabloon. Wordt nooit bewaard.</summary>
    public static Reservation Example(string language, DateOnly today)
    {
        var e = Examples.GetValueOrDefault(language) ?? Examples["nl"];
        var start = today.AddDays(14);
        return new Reservation
        {
            Number = $"{today.Year}-0042",
            Culture = language,
            StartDate = start,
            EndDate = start.AddDays(1),
            DeliveryMethod = DeliveryMethod.Delivery,
            DeliveryAddress = new Address { Street = "Kerkstraat 1", PostalCode = "9000", City = "Gent" },
            Notes = e.Notes,
            TotalPrice = 225,
            Customer = new Customer { FirstName = e.FirstName, LastName = e.LastName, Email = "klant@example.com" },
            Lines = [new ReservationLine { Quantity = 1, Description = e.Item, UnitPrice = 225 }],
        };
    }

    /// <summary>De taal van de mail: die van de reservatie, anders de standaardtaal van de verhuurder, anders Nederlands.</summary>
    public static string LanguageOf(Reservation reservation, Tenant tenant) =>
        ByCulture.ContainsKey(reservation.Culture) ? reservation.Culture
        : ByCulture.ContainsKey(tenant.DefaultCulture) ? tenant.DefaultCulture
        : "nl";

    /// <param name="template">Het mailsjabloon in de taal van <see cref="LanguageOf"/>: de aanpassing van de verhuurder of de standaardtekst.</param>
    public static MailMessage Create(Reservation reservation, Tenant tenant, MailTemplateText template)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var t = ByCulture[LanguageOf(reservation, tenant)];
        var culture = CultureInfo.GetCultureInfo(t.CultureName);

        var period = reservation.StartDate == reservation.EndDate
            ? Date(reservation.StartDate)
            : $"{Date(reservation.StartDate)} – {Date(reservation.EndDate)}";
        var values = new Dictionary<string, string>
        {
            ["firstName"] = customer.FirstName,
            ["lastName"] = customer.LastName,
            ["reservationNumber"] = reservation.Number,
            ["rentalPeriod"] = period,
            ["companyName"] = tenant.Name,
            ["contactEmail"] = tenant.ContactEmail ?? "",
            ["phone"] = tenant.ContactPhone ?? "",
        };
        string Fill(string text) => MailKindDefinition.Fill(text, name => values.GetValueOrDefault(name));

        var lines = reservation.Lines.Select(l => $"{l.Quantity} × {l.Description} – {Money(l.LineTotal)}").ToList();
        var delivery = reservation.DeliveryMethod == DeliveryMethod.Delivery && reservation.DeliveryAddress is { } a
            ? string.Format(t.DeliveryAt, $"{a.Street}, {a.PostalCode} {a.City}")
            : t.Pickup;
        var facts = new List<(string Label, string Value)> { (t.Number, reservation.Number), (t.Period, period) };

        var detailsText = new StringBuilder();
        foreach (var (label, value) in facts) detailsText.AppendLine($"{label}: {value}");
        detailsText.AppendLine().AppendLine($"{t.Contents}:");
        foreach (var line in lines) detailsText.AppendLine($"- {line}");
        detailsText.AppendLine($"{t.Total}: {Money(reservation.TotalPrice)}").AppendLine().Append(delivery);
        if (!string.IsNullOrWhiteSpace(reservation.Notes)) detailsText.AppendLine().Append($"{t.Notes}: {reservation.Notes}");

        var detailsHtml = new StringBuilder("<p>");
        foreach (var (label, value) in facts) detailsHtml.Append($"<strong>{E(label)}:</strong> {E(value)}<br>");
        detailsHtml.Append($"</p><p><strong>{E(t.Contents)}:</strong></p><ul>");
        foreach (var line in lines) detailsHtml.Append($"<li>{E(line)}</li>");
        detailsHtml.Append($"</ul><p><strong>{E(t.Total)}: {E(Money(reservation.TotalPrice))}</strong></p>")
            .Append($"<p>{E(delivery)}</p>");
        if (!string.IsNullOrWhiteSpace(reservation.Notes)) detailsHtml.Append($"<p><strong>{E(t.Notes)}:</strong> {E(reservation.Notes)}</p>");

        // Elke alinea van het sjabloon wordt een <p>; {reservationDetails} wordt het vaste blok, ook midden in een alinea.
        var text = new List<string>();
        var html = new StringBuilder();
        foreach (var paragraph in Paragraphs(template.Body))
        {
            var parts = paragraph.Split($"{{{MailKinds.ReservationDetails}}}");
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    text.Add(detailsText.ToString());
                    html.Append(detailsHtml);
                }
                var part = Fill(parts[i].Trim());
                if (part.Length == 0) continue;
                text.Add(part);
                html.Append($"<p>{E(part).ReplaceLineEndings("<br>")}</p>");
            }
        }

        return new MailMessage(customer.Email, Fill(template.Subject), html.ToString(),
            string.Join(Environment.NewLine + Environment.NewLine, text));

        string Date(DateOnly d) => d.ToString("d MMMM yyyy", culture);
        string Money(decimal amount) => amount.ToString("C", culture);
    }

    private static IEnumerable<string> Paragraphs(string body) =>
        body.ReplaceLineEndings("\n").Split("\n\n").Select(p => p.Trim()).Where(p => p.Length > 0);

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
