using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Localization;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Tenants;

namespace Moshtar.Web.Site;

/// <summary>
/// De bestanden voor zoekmachines en AI-bots, per verhuurder op zijn eigen domein: <c>robots.txt</c>,
/// <c>sitemap.xml</c> en <c>llms.txt</c> (https://llmstxt.org). Alleen in de root, niet onder /fr of /en.
/// </summary>
internal static class SiteFiles
{
    /// <summary>
    /// Bots die we bij naam welkom heten: zoekmachines en AI-bots. Ze krijgen dezelfde regels als alle andere; met
    /// hun naam erbij is duidelijk dat ze bewust toegelaten zijn.
    /// </summary>
    private static readonly string[] WelcomeBots =
    [
        "*", "Googlebot", "Bingbot", "Google-Extended", "GPTBot", "OAI-SearchBot", "ChatGPT-User",
        "ClaudeBot", "Claude-SearchBot", "Claude-User", "PerplexityBot", "Perplexity-User", "Applebot-Extended",
    ];

    public static IEndpointRouteBuilder MapSiteFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", Robots);
        app.MapGet("/sitemap.xml", SitemapAsync);
        app.MapGet("/llms.txt", LlmsAsync);
        return app;
    }

    private static IResult Robots(HttpContext context, SiteLinks links)
    {
        if (context.Request.PathBase.HasValue) return Results.NotFound();
        var text = new StringBuilder();
        foreach (var bot in WelcomeBots) text.Append("User-agent: ").Append(bot).Append('\n');
        text.Append("Allow: /\n");
        // Het back office, ook inloggen, staat helemaal onder /admin.
        text.Append("Disallow: /admin\n");
        text.Append('\n').Append("Sitemap: ").Append(SitemapUrl(links)).Append('\n');
        return Results.Text(text.ToString(), "text/plain; charset=utf-8");
    }

    private static async Task<IResult> SitemapAsync(HttpContext context, SiteLinks links, ICatalogService catalog, IWebsiteTexts texts)
    {
        if (context.Request.PathBase.HasValue) return Results.NotFound();
        var routes = await IndexedRoutesAsync(links, catalog, texts, context.RequestAborted);

        const string sitemaps = "http://www.sitemaps.org/schemas/sitemap/0.9";
        const string xhtml = "http://www.w3.org/1999/xhtml";
        var output = new StringBuilder();
        using (var xml = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", sitemaps);
            xml.WriteAttributeString("xmlns", "xhtml", null, xhtml);
            // Elke pagina in elke taal, telkens met de andere talen als alternatief (zoals de hreflang-links in de pagina).
            foreach (var route in routes)
            foreach (var culture in links.Cultures)
            {
                xml.WriteStartElement("url", sitemaps);
                xml.WriteElementString("loc", sitemaps, links.Absolute(route, culture));
                foreach (var alternate in links.Cultures)
                    WriteAlternate(xml, xhtml, alternate, links.Absolute(route, alternate));
                WriteAlternate(xml, xhtml, "x-default", links.Absolute(route, links.DefaultCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        // StringBuilder schrijft UTF-16 in de XML-declaratie; het antwoord is UTF-8.
        var body = output.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
        return Results.Text(body, "application/xml; charset=utf-8");
    }

    private static void WriteAlternate(XmlWriter xml, string xhtml, string hreflang, string href)
    {
        xml.WriteStartElement("xhtml", "link", xhtml);
        xml.WriteAttributeString("rel", "alternate");
        xml.WriteAttributeString("hreflang", hreflang);
        xml.WriteAttributeString("href", href);
        xml.WriteEndElement();
    }

    /// <summary>
    /// llms.txt: in markdown alles wat een AI-assistent nodig heeft om een vraag als "waar huur ik een springkasteel in
    /// Mortsel?" te beantwoorden, in de standaardtaal van de verhuurder.
    /// </summary>
    private static async Task<IResult> LlmsAsync(
        HttpContext context, ITenantContext tenantContext, SiteLinks links, ICatalogService catalog, IWebsiteTexts websiteTexts,
        IStringLocalizer<SharedResource> l)
    {
        if (context.Request.PathBase.HasValue) return Results.NotFound();
        var tenant = tenantContext.Tenant!;
        var culture = links.DefaultCulture;
        var texts = await websiteTexts.GetAsync(culture, context.RequestAborted);
        var entries = await catalog.GetCatalogAsync(culture, context.RequestAborted);
        var categories = (await CategoryLink.AllAsync(websiteTexts, links, context.RequestAborted))
            .Where(c => entries.Any(e => e.CategoryId == c.Id)).ToList();
        string Link(SiteRoute route) => links.Absolute(route, culture);

        var md = new StringBuilder();
        md.Append("# ").Append(tenant.Name).Append("\n\n");
        md.Append("> ").Append(OneLine(texts.HomeSubtitle.Value)).Append("\n\n");
        md.Append(OneLine(texts.HomeTitle.Value)).Append(": ").Append(Link(SiteRoute.Home)).Append('\n');
        var others = links.Cultures.Where(c => c != culture).ToList();
        if (others.Count > 0)
            md.Append(l["OtherLanguages"]).Append(": ")
                .AppendJoin(", ", others.Select(c => $"[{CultureInfo.GetCultureInfo(c).NativeName}]({links.Absolute(SiteRoute.Home, c)})"))
                .Append('\n');

        md.Append("\n## ").Append(l["Contact"]).Append("\n\n");
        var address = string.Join(", ", new[] { tenant.Street, $"{tenant.PostalCode} {tenant.City}".Trim() }.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (address.Length > 0) md.Append("- ").Append(l["Address"]).Append(": ").Append(address).Append('\n');
        if (BusinessDetails.Phone(tenant.ContactPhone) is { } phone) md.Append("- ").Append(l["Phone"]).Append(": ").Append(phone).Append('\n');
        if (BusinessDetails.Phone(tenant.WhatsAppPhone) is { } whatsApp) md.Append("- WhatsApp: ").Append(whatsApp).Append('\n');
        if (BusinessDetails.Email(tenant.ContactEmail) is { } email) md.Append("- ").Append(l["Email"]).Append(": ").Append(email).Append('\n');
        if (!string.IsNullOrWhiteSpace(tenant.VatNumber)) md.Append("- ").Append(l["VatNumber"]).Append(": ").Append(tenant.VatNumber).Append('\n');
        md.Append("- [").Append(l["Contact"]).Append("](").Append(Link(SiteRoute.Contact)).Append(")\n");

        if (tenant.ServiceArea.Count > 0)
            md.Append("\n## ").Append(l["ServiceAreaTitle"]).Append("\n\n").AppendJoin(", ", tenant.ServiceArea).Append('\n');

        md.Append("\n## ").Append(l["Categories"]).Append("\n\n");
        foreach (var category in categories)
        {
            md.Append("### [").Append(category.Name).Append("](").Append(Link(category.Route)).Append(")\n\n");
            if (category.Intro.Length > 0) md.Append(category.Intro.Trim()).Append("\n\n");
            foreach (var entry in entries.Where(e => e.CategoryId == category.Id)) AppendEntry(md, entry, links, l, culture);
            md.Append('\n');
        }
        var uncategorized = entries.Where(e => e.Kind == CatalogEntryKind.Item && (e.CategoryId is null || categories.All(c => c.Id != e.CategoryId))).ToList();
        if (uncategorized.Count > 0)
        {
            md.Append("### ").Append(l["OtherItems"]).Append("\n\n");
            foreach (var entry in uncategorized) AppendEntry(md, entry, links, l, culture);
            md.Append('\n');
        }

        var bundles = entries.Where(e => e.Kind == CatalogEntryKind.Bundle).ToList();
        if (bundles.Count > 0)
        {
            md.Append("## ").Append(l["Bundles"]).Append("\n\n");
            foreach (var entry in bundles) AppendEntry(md, entry, links, l, culture);
            md.Append('\n');
        }

        if (texts.Faq.Count > 0)
        {
            md.Append("## [").Append(l["Faq"]).Append("](").Append(Link(SiteRoute.Faq)).Append(")\n\n");
            foreach (var entry in texts.Faq)
                md.Append("### ").Append(OneLine(entry.Question)).Append("\n\n").Append(entry.Answer.Trim()).Append("\n\n");
        }

        return Results.Text(md.ToString().TrimEnd() + "\n", "text/plain; charset=utf-8");
    }

    /// <summary>"- [Springkasteel Jungle](url): € 150,00 per dag (inclusief btw). Groot springkasteel …"</summary>
    private static void AppendEntry(StringBuilder md, CatalogEntry entry, SiteLinks links, IStringLocalizer l, string culture)
    {
        md.Append("- [").Append(entry.Name).Append("](").Append(links.Absolute(new RentalRoute(entry.Slug), culture)).Append("): ")
            .Append(SiteText.Money(entry.DayPrice)).Append(' ').Append(l["PerDay"]).Append(" (").Append(l["VatIncluded"]).Append(')');
        if (entry.Description is { Length: > 0 } description) md.Append(". ").Append(OneLine(description).TrimEnd('.'));
        if (entry.Contents.Count > 0) md.Append(". ").Append(l["BundleContains"]).Append(": ").AppendJoin(", ", entry.Contents);
        md.Append('\n');
    }

    /// <summary>
    /// De pagina's die in de sitemap horen: home, categorieën met artikelen, artikelen, pakketten, veelgestelde vragen
    /// contact en de privacyverklaring. Zonder huurperiode: die pagina's worden niet geïndexeerd.
    /// </summary>
    private static async Task<IReadOnlyList<SiteRoute>> IndexedRoutesAsync(SiteLinks links, ICatalogService catalog, IWebsiteTexts texts, CancellationToken ct)
    {
        var entries = await catalog.GetCatalogAsync(links.DefaultCulture, ct);
        var categories = await CategoryLink.AllAsync(texts, links, ct);
        var faq = (await texts.GetAsync(links.DefaultCulture, ct)).Faq;
        return new[] { SiteRoute.Home }
            .Concat(categories.Where(c => entries.Any(e => e.CategoryId == c.Id)).Select(c => (SiteRoute)c.Route))
            .Concat(entries.Select(e => (SiteRoute)new RentalRoute(e.Slug)))
            .Concat(faq.Count > 0 ? [SiteRoute.Faq] : [])
            .Append(SiteRoute.Contact)
            .Append(SiteRoute.Privacy)
            .ToList();
    }

    private static string SitemapUrl(SiteLinks links) => links.Absolute(SiteRoute.Home, links.DefaultCulture).TrimEnd('/') + "/sitemap.xml";

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
