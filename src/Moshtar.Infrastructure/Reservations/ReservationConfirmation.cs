using System.Globalization;
using System.Net;
using System.Text;
using Moshtar.Application.Mail;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;

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
        string Notes,
        string ReplacedBy,
        string Surcharge);

    private static readonly Dictionary<string, DetailTexts> ByCulture = new()
    {
        ["nl"] = new("nl-BE", "Reservatienummer", "Huurperiode", "Wat je huurt", "Totaal", "Levering op {0}", "Afhalen", "Opmerkingen", "{0} vervangen door {1}", "Meerprijs {0} i.p.v. {1}"),
        ["fr"] = new("fr-BE", "Numéro de réservation", "Période de location", "Votre location", "Total", "Livraison à {0}", "Retrait sur place", "Remarques", "{0} remplacé par {1}", "Supplément {0} au lieu de {1}"),
        ["en"] = new("en-BE", "Reservation number", "Rental period", "What you are renting", "Total", "Delivery to {0}", "Pickup", "Notes", "{0} replaced by {1}", "Extra charge {0} instead of {1}"),
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
            Customer = new Customer
            {
                FirstName = e.FirstName, LastName = e.LastName, Email = "klant@example.com",
                Address = new Address { Street = "Kerkstraat 1", PostalCode = "9000", City = "Gent" },
            },
            Lines = [new ReservationLine { Quantity = 1, Description = e.Item, UnitPrice = 225 }],
        };
    }

    /// <summary>De taal van de mail: die van de reservatie, anders de standaardtaal van de verhuurder, anders Nederlands.</summary>
    public static string LanguageOf(Reservation reservation, Tenant tenant) =>
        ByCulture.ContainsKey(reservation.Culture) ? reservation.Culture
        : ByCulture.ContainsKey(tenant.DefaultCulture) ? tenant.DefaultCulture
        : "nl";

    /// <param name="template">Het mailsjabloon in de taal van <see cref="LanguageOf"/>: de aanpassing van de verhuurder of de standaardtekst.</param>
    /// <param name="logoUrl">Het publieke adres van het logo van de verhuurder, of null.</param>
    public static MailMessage Create(Reservation reservation, Tenant tenant, string? logoUrl, MailTemplateText template)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var values = Values(reservation, tenant);
        var (html, text) = MailBody.Compose(tenant, logoUrl, template.Body, name => values.GetValueOrDefault(name),
            new Dictionary<string, MailBlock> { [MailKinds.ReservationDetails] = Details(reservation, tenant) });
        return new MailMessage(customer.Email, MailKindDefinition.Fill(template.Subject, name => values.GetValueOrDefault(name)), html, text);
    }

    /// <summary>De gewone reservatieplaatshouders (klant met bedrijf, btw-nummer en factuuradres, nummer, huurperiode, verhuurder), in de taal van <see cref="LanguageOf"/>.</summary>
    public static Dictionary<string, string> Values(Reservation reservation, Tenant tenant)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        return new Dictionary<string, string>
        {
            ["firstName"] = customer.FirstName,
            ["lastName"] = customer.LastName,
            ["reservationNumber"] = reservation.Number,
            ["rentalPeriod"] = Period(reservation, CultureOf(reservation, tenant)),
            ["companyName"] = tenant.Name,
            ["contactEmail"] = tenant.ContactEmail ?? "",
            ["phone"] = tenant.ContactPhone ?? "",
            [MailKinds.CustomerCompanyName] = customer.CompanyName ?? "",
            [MailKinds.CustomerVatNumber] = customer.VatNumber ?? "",
            [MailKinds.BillingAddress] = AddressLine(customer.BillingAddressOrAddress),
        };
    }

    /// <summary>Een adres op één regel, zoals in het overzicht van de reservatie: "Kerkstraat 1, 9000 Gent".</summary>
    private static string AddressLine(Address a) => $"{a.Street}, {a.PostalCode} {a.City}";

    private static CultureInfo CultureOf(Reservation reservation, Tenant tenant) =>
        CultureInfo.GetCultureInfo(ByCulture[LanguageOf(reservation, tenant)].CultureName);

    private static string Period(Reservation reservation, CultureInfo culture) =>
        reservation.StartDate == reservation.EndDate
            ? reservation.StartDate.ToString("d MMMM yyyy", culture)
            : $"{reservation.StartDate.ToString("d MMMM yyyy", culture)} – {reservation.EndDate.ToString("d MMMM yyyy", culture)}";

    /// <summary>De omschrijving van de meerprijslijn bij het vervangen, in de taal van <see cref="LanguageOf"/>: "Meerprijs Piraat i.p.v. Jungle".</summary>
    public static string SurchargeDescription(Reservation reservation, Tenant tenant, string replacementName, string name) =>
        string.Format(ByCulture[LanguageOf(reservation, tenant)].Surcharge, replacementName, name);

    /// <summary>Een lijn zoals de klant ze ziet, met de artikelen die binnen het pakket vervangen zijn.</summary>
    private static string LineText(ReservationLine line, DetailTexts t, CultureInfo culture)
    {
        // Een kortingslijn zoals in de winkelkar en het back office: "2e springkasteel 40% korting: −€ 40,00".
        if (line.IsDiscount) return $"{line.Description}: −{(-line.LineTotal).ToString("C", culture)}";
        var text = $"{line.Quantity} × {line.Description}";
        if (line.Replacements.Count > 0)
            text += $" ({string.Join(", ", line.Replacements.Select(r => string.Format(t.ReplacedBy, r.RentalItemName, r.ReplacementName)))})";
        return $"{text} – {line.LineTotal.ToString("C", culture)}";
    }

    /// <summary>Het vaste overzicht van de reservatie ({reservationDetails}), in de taal van <see cref="LanguageOf"/>.</summary>
    public static MailBlock Details(Reservation reservation, Tenant tenant)
    {
        var t = ByCulture[LanguageOf(reservation, tenant)];
        var culture = CultureInfo.GetCultureInfo(t.CultureName);
        var period = Period(reservation, culture);

        var lines = reservation.Lines.Select(l => LineText(l, t, culture)).ToList();
        var delivery = reservation.DeliveryMethod == DeliveryMethod.Delivery && reservation.DeliveryAddress is { } a
            ? string.Format(t.DeliveryAt, AddressLine(a))
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

        return new MailBlock(detailsHtml.ToString(), detailsText.ToString());

        string Money(decimal amount) => amount.ToString("C", culture);
    }

    /// <summary>De cultuur voor datums en bedragen in een mail in deze taal (zie <see cref="LanguageOf"/>).</summary>
    public static CultureInfo CultureOf(string language) =>
        CultureInfo.GetCultureInfo((ByCulture.GetValueOrDefault(language) ?? ByCulture["nl"]).CultureName);

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
