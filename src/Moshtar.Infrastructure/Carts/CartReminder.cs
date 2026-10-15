using System.Globalization;
using System.Net;
using System.Text;
using Moshtar.Application.Carts;
using Moshtar.Application.Catalog;
using Moshtar.Application.Mail;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;

namespace Moshtar.Infrastructure.Carts;

/// <summary>
/// De herinneringsmail voor een winkelkar die niet gereserveerd werd, in de taal van de klant, opgebouwd uit het
/// mailsjabloon. Wat in de winkelkar zit ({cartContents}) en de link ernaartoe ({cartLink}) zijn vaste blokken.
/// </summary>
internal static class CartReminder
{
    private sealed record Texts(string CultureName, string Period, string Contents, string Total, string NotAvailable, string Link);

    private static readonly Dictionary<string, Texts> ByCulture = new()
    {
        ["nl"] = new("nl-BE", "Huurperiode", "In je winkelkar", "Totaal", "niet meer vrij", "Naar je winkelkar"),
        ["fr"] = new("fr-BE", "Période de location", "Dans votre panier", "Total", "plus disponible", "Voir votre panier"),
        ["en"] = new("en-BE", "Rental period", "In your cart", "Total", "no longer available", "Go to your cart"),
    };

    private static readonly Dictionary<string, string> ExampleItems = new()
    {
        ["nl"] = "Springkasteel Piraat",
        ["fr"] = "Château gonflable Pirate",
        ["en"] = "Bouncy castle Pirate",
    };

    /// <summary>De taal van de mail: die van de klant, anders de standaardtaal van de verhuurder, anders Nederlands.</summary>
    public static string LanguageOf(string? culture, Tenant tenant) =>
        culture is not null && ByCulture.ContainsKey(culture) ? culture
        : ByCulture.ContainsKey(tenant.DefaultCulture) ? tenant.DefaultCulture
        : "nl";

    /// <summary>Een fictieve winkelkar in deze taal, voor het voorbeeld en de testmail van het mailsjabloon.</summary>
    public static CartView Example(string language, DateOnly today)
    {
        var name = ExampleItems.GetValueOrDefault(language) ?? ExampleItems["nl"];
        var entry = new CatalogEntry(Guid.Empty, CatalogEntryKind.Item, "voorbeeld", name, null, null, null, 150, 75, [], []);
        var start = today.AddDays(14);
        return new CartView(new DateRange(start, start.AddDays(1)), [new CartLineView(CatalogRef.Item(Guid.Empty), entry, 1, 225, true)],
            "klant@example.com", WantsReminder: true);
    }

    /// <param name="language">De taal van <see cref="LanguageOf"/>.</param>
    /// <param name="template">Het mailsjabloon in die taal: de aanpassing van de verhuurder of de standaardtekst.</param>
    /// <param name="link">Het volledige adres dat de winkelkar opent, ook op een ander toestel.</param>
    /// <param name="logoUrl">Het publieke adres van het logo van de verhuurder, of null.</param>
    public static MailMessage Create(CartView cart, string language, Tenant tenant, string? logoUrl, MailTemplateText template, string link)
    {
        var t = ByCulture[language];
        var culture = CultureInfo.GetCultureInfo(t.CultureName);
        var period = cart.Period.Start == cart.Period.End
            ? Date(cart.Period.Start)
            : $"{Date(cart.Period.Start)} – {Date(cart.Period.End)}";
        var values = new Dictionary<string, string>
        {
            ["rentalPeriod"] = period,
            ["companyName"] = tenant.Name,
            ["contactEmail"] = tenant.ContactEmail ?? "",
            ["phone"] = tenant.ContactPhone ?? "",
        };

        var lines = cart.Lines.Where(l => l.Entry is not null)
            .Select(l => $"{l.Quantity} × {l.Entry!.Name} – {Money(l.LineTotal)}{(l.Available ? "" : $" ({t.NotAvailable})")}")
            // De kortingen als eigen lijnen, zoals in de winkelkar en de bevestigingsmail.
            .Concat(cart.DiscountLines.Select(d => $"{d.Name}: −{Money(d.Amount)}"))
            .ToList();
        var contentsText = new StringBuilder()
            .AppendLine($"{t.Period}: {period}").AppendLine().AppendLine($"{t.Contents}:");
        foreach (var line in lines) contentsText.AppendLine($"- {line}");
        contentsText.Append($"{t.Total}: {Money(cart.Total)}");

        var contentsHtml = new StringBuilder($"<p><strong>{E(t.Period)}:</strong> {E(period)}</p><p><strong>{E(t.Contents)}:</strong></p><ul>");
        foreach (var line in lines) contentsHtml.Append($"<li>{E(line)}</li>");
        contentsHtml.Append($"</ul><p><strong>{E(t.Total)}: {E(Money(cart.Total))}</strong></p>");

        var linkHtml = $"<p><a href=\"{E(link)}\" style=\"display:inline-block;padding:12px 20px;background-color:#1f2937;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold\">{E(t.Link)}</a></p>";

        var (html, text) = MailBody.Compose(tenant, logoUrl, template.Body, name => values.GetValueOrDefault(name),
            new Dictionary<string, MailBlock>
            {
                [MailKinds.CartContents] = new(contentsHtml.ToString(), contentsText.ToString()),
                [MailKinds.CartLink] = new(linkHtml, $"{t.Link}: {link}"),
            });
        var subject = MailKindDefinition.Fill(template.Subject, name => values.GetValueOrDefault(name));
        return new MailMessage(cart.Email ?? "", subject, html, text);

        string Date(DateOnly d) => d.ToString("d MMMM yyyy", culture);
        string Money(decimal amount) => amount.ToString("C", culture);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
