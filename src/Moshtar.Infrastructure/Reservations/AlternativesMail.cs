using System.Globalization;
using System.Net;
using System.Text;
using Moshtar.Application.Mail;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>Een alternatief zoals het in de mail komt, in de taal van de klant.</summary>
/// <param name="PhotoUrl">Het volledige publieke adres van de hoofdfoto, of null.</param>
/// <param name="Surcharge">De meerprijs, of null: zonder meerprijs.</param>
/// <param name="Link">Het volledige adres van de artikelpagina in de taal van de klant.</param>
internal sealed record MailedAlternative(string Name, string? PhotoUrl, decimal? Surcharge, string Link);

/// <summary>
/// De mail met alternatieven voor een artikel dat in een reservatie niet verhuurbaar is, in de taal van de klant,
/// opgebouwd uit het mailsjabloon. De alternatieven ({alternatives}) en het overzicht van de reservatie
/// ({reservationDetails}) zijn vaste blokken.
/// </summary>
internal static class AlternativesMail
{
    private sealed record Texts(string CultureName, string NoSurcharge, string Surcharge, string View);

    private static readonly Dictionary<string, Texts> ByCulture = new()
    {
        ["nl"] = new("nl-BE", "zonder meerprijs", "meerprijs {0}", "Bekijk op de website"),
        ["fr"] = new("fr-BE", "sans supplément", "supplément de {0}", "Voir sur le site"),
        ["en"] = new("en-BE", "no extra charge", "extra charge of {0}", "View on the website"),
    };

    private sealed record ExampleTexts(string Unavailable, string First, string Second);

    private static readonly Dictionary<string, ExampleTexts> Examples = new()
    {
        ["nl"] = new("Springkasteel Piraat", "Springkasteel Jungle", "Springkasteel Prinses"),
        ["fr"] = new("Château gonflable Pirate", "Château gonflable Jungle", "Château gonflable Princesse"),
        ["en"] = new("Bouncy castle Pirate", "Bouncy castle Jungle", "Bouncy castle Princess"),
    };

    /// <summary>De naam van het niet verhuurbare artikel en twee alternatieven, voor het voorbeeld en de testmail.</summary>
    public static (string Unavailable, IReadOnlyList<MailedAlternative> Alternatives) Example(string language, string host, string defaultCulture)
    {
        var e = Examples.GetValueOrDefault(language) ?? Examples["nl"];
        var path = Application.Catalog.RentalPath.For("voorbeeld", language, defaultCulture);
        return (e.Unavailable, [new(e.First, null, null, $"https://{host}{path}"), new(e.Second, null, 25, $"https://{host}{path}")]);
    }

    /// <param name="template">Het mailsjabloon in de taal van <see cref="ReservationConfirmation.LanguageOf"/>.</param>
    /// <param name="unavailableItem">De naam van het artikel dat niet verhuurbaar is, in de taal van de klant.</param>
    /// <param name="logoUrl">Het publieke adres van het logo van de verhuurder, of null.</param>
    public static MailMessage Create(
        Reservation reservation, Tenant tenant, string? logoUrl, MailTemplateText template, string unavailableItem, IReadOnlyList<MailedAlternative> alternatives)
    {
        var customer = reservation.Customer ?? throw new InvalidOperationException("Reservatie zonder klant.");
        var t = ByCulture[ReservationConfirmation.LanguageOf(reservation, tenant)];
        var culture = CultureInfo.GetCultureInfo(t.CultureName);
        var values = ReservationConfirmation.Values(reservation, tenant);
        values["unavailableItem"] = unavailableItem;

        var text = new StringBuilder();
        var html = new StringBuilder("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:8px 0\">");
        foreach (var alternative in alternatives)
        {
            var price = alternative.Surcharge is { } surcharge ? string.Format(t.Surcharge, surcharge.ToString("C", culture)) : t.NoSurcharge;
            text.AppendLine($"- {alternative.Name} – {price}").AppendLine($"  {t.View}: {alternative.Link}");
            html.Append("<tr><td style=\"padding:8px 16px 8px 0;vertical-align:top\">");
            if (alternative.PhotoUrl is { } photo)
                html.Append($"<a href=\"{E(alternative.Link)}\"><img src=\"{E(photo)}\" alt=\"{E(alternative.Name)}\" width=\"160\" style=\"display:block;width:160px;max-width:160px;height:auto;border-radius:6px\"></a>");
            html.Append("</td><td style=\"padding:8px 0;vertical-align:top\">")
                .Append($"<strong>{E(alternative.Name)}</strong><br>{E(price)}<br>")
                .Append($"<a href=\"{E(alternative.Link)}\">{E(t.View)}</a></td></tr>");
        }
        html.Append("</table>");

        var (bodyHtml, bodyText) = MailBody.Compose(tenant, logoUrl, template.Body, name => values.GetValueOrDefault(name),
            new Dictionary<string, MailBlock>
            {
                [MailKinds.Alternatives] = new(html.ToString(), text.ToString().TrimEnd()),
                [MailKinds.ReservationDetails] = ReservationConfirmation.Details(reservation, tenant),
            });
        return new MailMessage(customer.Email, MailKindDefinition.Fill(template.Subject, name => values.GetValueOrDefault(name)), bodyHtml, bodyText);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
