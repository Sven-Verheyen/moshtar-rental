using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Tenants;
using Moshtar.Domain.Website;

namespace Moshtar.Web.Site;

/// <summary>
/// Gestructureerde gegevens (JSON-LD, schema.org) voor zoekmachines en AI-bots: de verhuurder als LocalBusiness op
/// elke pagina, een artikel of pakket als Product met Offer, de veelgestelde vragen als FAQPage en het kruimelpad
/// als BreadcrumbList.
/// </summary>
public static class StructuredData
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>
    /// Het script om in de pagina te zetten. De standaard-encoder van System.Text.Json schrijft &lt;, &gt; en &amp; als
    /// < enz., zodat een naam als "&lt;/script&gt;" het script niet kan afsluiten.
    /// </summary>
    public static string Script(JsonObject data) =>
        $"<script type=\"application/ld+json\">{data.ToJsonString(Options)}</script>";

    /// <summary>Hetzelfde id op elke pagina, zodat een Offer naar de verhuurder kan verwijzen.</summary>
    public static string BusinessId(SiteLinks links) => links.Absolute(SiteRoute.Home, links.DefaultCulture) + "#verhuurder";

    public static JsonObject LocalBusiness(Tenant tenant, SiteLinks links, IImageStore images)
    {
        var data = Node("LocalBusiness");
        data["@id"] = BusinessId(links);
        data["name"] = tenant.Name;
        data["url"] = links.Absolute(SiteRoute.Home, links.Culture);
        Add(data, "logo", images.LogoUrl(tenant));
        Add(data, "image", images.HeroImageUrl(tenant) ?? images.LogoUrl(tenant));
        Add(data, "telephone", BusinessDetails.Phone(tenant.ContactPhone));
        Add(data, "email", BusinessDetails.Email(tenant.ContactEmail));
        Add(data, "vatID", tenant.VatNumber);
        if (tenant.Street is not null || tenant.PostalCode is not null || tenant.City is not null)
        {
            var address = Typed("PostalAddress");
            Add(address, "streetAddress", tenant.Street);
            Add(address, "postalCode", tenant.PostalCode);
            Add(address, "addressLocality", tenant.City);
            data["address"] = address;
        }
        if (tenant.ServiceArea.Count > 0)
            data["areaServed"] = new JsonArray(tenant.ServiceArea.Select(m => (JsonNode)Named("City", m)).ToArray());
        var sameAs = new[] { tenant.FacebookUrl, tenant.InstagramUrl, tenant.TikTokUrl, tenant.GoogleBusinessUrl }
            .Select(BusinessDetails.Link).OfType<string>().ToList();
        if (sameAs.Count > 0)
            data["sameAs"] = new JsonArray(sameAs.Select(url => (JsonNode)JsonValue.Create(url)!).ToArray());
        return data;
    }

    /// <summary>
    /// Een artikel of pakket met zijn dagprijs, inclusief btw. Het is een verhuring (LeaseOut), geen verkoop; de prijs
    /// geldt per dag (UN/CEFACT-code DAY). <paramref name="dayPrice"/>: de dagprijs die de bezoeker ziet, met een actie
    /// die niet aan huurperiodes gebonden is; anders de gewone dagprijs.
    /// </summary>
    public static JsonObject Product(CatalogEntry entry, SiteLinks links, IImageStore images, decimal? dayPrice = null)
    {
        var url = links.Absolute(new RentalRoute(entry.Slug), links.Culture);
        var data = Node("Product");
        data["name"] = entry.Name;
        Add(data, "description", entry.Description);
        if (entry.Photos.Count > 0)
            data["image"] = new JsonArray(entry.Photos.Select(key => (JsonNode)JsonValue.Create(images.ShareUrl(key))!).ToArray());
        Add(data, "category", entry.Category);
        data["url"] = url;
        data["sku"] = entry.Slug;

        var price = (dayPrice ?? entry.DayPrice).ToString("0.00", CultureInfo.InvariantCulture);
        var specification = Typed("UnitPriceSpecification");
        specification["price"] = price;
        specification["priceCurrency"] = "EUR";
        specification["unitCode"] = "DAY";
        specification["valueAddedTaxIncluded"] = true;

        var offer = Typed("Offer");
        offer["url"] = url;
        offer["price"] = price;
        offer["priceCurrency"] = "EUR";
        // Of het vandaag ergens vrij is, hangt van de datum af; dit zegt alleen of de verhuurder het kan verhuren.
        offer["availability"] = entry.InStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock";
        offer["businessFunction"] = "http://purl.org/goodrelations/v1#LeaseOut";
        offer["priceSpecification"] = specification;
        offer["seller"] = new JsonObject { ["@id"] = BusinessId(links) };
        data["offers"] = offer;
        return data;
    }

    public static JsonObject FaqPage(IReadOnlyList<FaqEntry> faq)
    {
        var data = Node("FAQPage");
        data["mainEntity"] = new JsonArray(faq.Select(e =>
        {
            var question = Named("Question", e.Question);
            var answer = Typed("Answer");
            answer["text"] = e.Answer;
            question["acceptedAnswer"] = answer;
            return (JsonNode)question;
        }).ToArray());
        return data;
    }

    /// <summary>Het kruimelpad: de home, eventueel de categorie, en de pagina zelf.</summary>
    public static JsonObject Breadcrumbs(params (string Name, string Url)[] trail)
    {
        var data = Node("BreadcrumbList");
        data["itemListElement"] = new JsonArray(trail.Select((step, i) =>
        {
            var item = Named("ListItem", step.Name);
            item["position"] = i + 1;
            item["item"] = step.Url;
            return (JsonNode)item;
        }).ToArray());
        return data;
    }

    private static JsonObject Node(string type) => new() { ["@context"] = "https://schema.org", ["@type"] = type };

    /// <summary>Een object binnen een ander: zonder eigen @context.</summary>
    private static JsonObject Typed(string type) => new() { ["@type"] = type };

    private static JsonObject Named(string type, string name) => new() { ["@type"] = type, ["name"] = name };

    private static void Add(JsonObject data, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) data[key] = value;
    }
}
