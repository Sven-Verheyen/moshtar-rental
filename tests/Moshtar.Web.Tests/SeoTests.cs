using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Zoekmachines en AI-bots vinden het aanbod van een verhuurder: titels, beschrijvingen en Open Graph op elke pagina,
/// gestructureerde gegevens (JSON-LD), en per verhuurder een robots.txt, sitemap.xml en llms.txt.
/// </summary>
public sealed partial class SeoTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Every_page_has_its_own_title_and_description_in_the_language_of_the_url()
    {
        await NewTenantAsync("seo-titels");
        var pages = new Dictionary<string, string>
        {
            ["/"] = "Springkastelen en feestspellen huren bij Seo Verhuur",
            ["/springkastelen"] = "Springkastelen · Seo Verhuur",
            ["/huren/piraat"] = "Piratenkasteel · Seo Verhuur",
            ["/huren/feest"] = "Feestpakket · Seo Verhuur",
            ["/veelgestelde-vragen"] = "Veelgestelde vragen · Seo Verhuur",
            ["/contact"] = "Contact · Seo Verhuur",
            ["/fr/questions-frequentes"] = "Questions fréquentes · Seo Verhuur",
        };

        foreach (var (path, title) in pages)
        {
            var html = await PageAsync("seo-titels", path);
            Assert.Contains($"<title>{title}</title>", html);
            Assert.Matches("<meta name=\"description\" content=\"[^\"]+\" />", html);
            Assert.Contains($"<meta property=\"og:title\" content=\"{title.Split(" · ")[0]}\" />", html);
            Assert.Contains("<meta property=\"og:url\" content=\"https://seo-titels.test", html);
        }
        Assert.Contains("<meta property=\"og:locale\" content=\"fr_BE\" />", await PageAsync("seo-titels", "/fr/questions-frequentes"));
    }

    [Fact]
    public async Task The_item_page_describes_the_item_as_a_product_with_a_rental_price_per_day()
    {
        await NewTenantAsync("seo-artikel");

        var html = await PageAsync("seo-artikel", "/huren/piraat");

        Assert.Contains("<meta property=\"og:type\" content=\"product\" />", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://seo.test/piraat.jpg\" />", html);
        var product = JsonLd(html, "Product");
        Assert.Equal("Piratenkasteel", product.GetProperty("name").GetString());
        Assert.Equal("Een kasteel met een mast.", product.GetProperty("description").GetString());
        Assert.Equal("https://seo.test/piraat.jpg", product.GetProperty("image")[0].GetString());
        var offer = product.GetProperty("offers");
        Assert.Equal(("120.00", "EUR", "https://schema.org/InStock"),
            (offer.GetProperty("price").GetString(), offer.GetProperty("priceCurrency").GetString(), offer.GetProperty("availability").GetString()));
        var price = offer.GetProperty("priceSpecification");
        Assert.Equal("DAY", price.GetProperty("unitCode").GetString());
        Assert.True(price.GetProperty("valueAddedTaxIncluded").GetBoolean());
        Assert.Equal("https://seo-artikel.test/#verhuurder", offer.GetProperty("seller").GetProperty("@id").GetString());

        var breadcrumbs = JsonLd(html, "BreadcrumbList").GetProperty("itemListElement").EnumerateArray()
            .Select(i => (i.GetProperty("name").GetString(), i.GetProperty("item").GetString())).ToList();
        Assert.Equal(
            [("Seo Verhuur", "https://seo-artikel.test/"), ("Springkastelen", "https://seo-artikel.test/springkastelen"), ("Piratenkasteel", "https://seo-artikel.test/huren/piraat")],
            breadcrumbs);
    }

    [Fact]
    public async Task Every_page_describes_the_rental_company_as_a_local_business()
    {
        await NewTenantAsync("seo-bedrijf");

        foreach (var path in new[] { "/", "/springkastelen", "/huren/piraat", "/contact", "/en/faq" })
        {
            var business = JsonLd(await PageAsync("seo-bedrijf", path), "LocalBusiness");

            Assert.Equal("Seo Verhuur", business.GetProperty("name").GetString());
            Assert.Equal("+32470123456", business.GetProperty("telephone").GetString());
            Assert.Equal("https://seo.test/logo.png", business.GetProperty("logo").GetString());
            Assert.Equal("Kerkstraat 1", business.GetProperty("address").GetProperty("streetAddress").GetString());
            Assert.Equal("Mortsel", business.GetProperty("address").GetProperty("addressLocality").GetString());
            Assert.Equal(["Mortsel", "Edegem"], business.GetProperty("areaServed").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
            Assert.Equal(["https://www.facebook.com/seo"], business.GetProperty("sameAs").EnumerateArray().Select(a => a.GetString()));
        }
    }

    [Fact]
    public async Task The_faq_page_and_the_category_page_have_their_own_structured_data()
    {
        await NewTenantAsync("seo-faq");

        var faq = JsonLd(await PageAsync("seo-faq", "/veelgestelde-vragen"), "FAQPage");
        var category = JsonLd(await PageAsync("seo-faq", "/springkastelen"), "BreadcrumbList");

        var questions = faq.GetProperty("mainEntity").EnumerateArray().ToList();
        Assert.Equal(5, questions.Count);
        Assert.Equal("Question", questions[0].GetProperty("@type").GetString());
        Assert.False(string.IsNullOrEmpty(questions[0].GetProperty("acceptedAnswer").GetProperty("text").GetString()));
        Assert.Equal(2, category.GetProperty("itemListElement").GetArrayLength());
    }

    [Fact]
    public async Task Structured_data_cannot_break_out_of_its_script()
    {
        await NewTenantAsync("seo-script", itemName: "Kasteel </script><script>alert(1)</script>");

        var html = await app.CreateClient("seo-script.test").GetStringAsync("/huren/piraat");

        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.Equal("Kasteel </script><script>alert(1)</script>", JsonLd(html, "Product").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Robots_txt_welcomes_search_engines_and_ai_bots_but_not_in_the_back_office()
    {
        await NewTenantAsync("seo-robots");
        var client = app.CreateClient("seo-robots.test");

        var response = await client.GetAsync("/robots.txt");
        var robots = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        foreach (var bot in new[] { "*", "GPTBot", "ClaudeBot", "PerplexityBot", "Google-Extended" })
            Assert.Contains($"User-agent: {bot}\n", robots);
        Assert.Contains("Allow: /\n", robots);
        Assert.Contains("Disallow: /admin\n", robots);
        Assert.Contains("Sitemap: https://seo-robots.test/sitemap.xml\n", robots);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/fr/robots.txt")).StatusCode);
    }

    [Fact]
    public async Task The_sitemap_lists_every_page_in_every_language_with_its_alternates()
    {
        await NewTenantAsync("seo-sitemap");

        var response = await app.CreateClient("seo-sitemap.test").GetAsync("/sitemap.xml");
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());

        XNamespace sitemaps = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var urls = xml.Root!.Elements(sitemaps + "url").ToList();
        var locs = urls.Select(u => u.Element(sitemaps + "loc")!.Value).ToList();
        // Home, één categorie met artikelen (spellen heeft er geen), drie artikelen, een pakket, vragen en contact: 8 pagina's in 3 talen.
        Assert.Equal(24, locs.Count);
        Assert.Equal(locs.Count, locs.Distinct().Count());
        foreach (var loc in new[]
        {
            "https://seo-sitemap.test/", "https://seo-sitemap.test/fr", "https://seo-sitemap.test/springkastelen",
            "https://seo-sitemap.test/en/bouncy-castles", "https://seo-sitemap.test/fr/louer/piraat", "https://seo-sitemap.test/huren/feest",
            "https://seo-sitemap.test/en/faq", "https://seo-sitemap.test/contact",
        })
            Assert.Contains(loc, locs);
        Assert.DoesNotContain("https://seo-sitemap.test/spellen", locs);
        var castle = urls.Single(u => u.Element(sitemaps + "loc")!.Value == "https://seo-sitemap.test/fr/louer/piraat");
        Assert.Equal(
            ["nl https://seo-sitemap.test/huren/piraat", "fr https://seo-sitemap.test/fr/louer/piraat", "en https://seo-sitemap.test/en/rent/piraat", "x-default https://seo-sitemap.test/huren/piraat"],
            castle.Elements(xhtml + "link").Select(l => $"{l.Attribute("hreflang")!.Value} {l.Attribute("href")!.Value}"));
    }

    [Fact]
    public async Task Llms_txt_tells_an_ai_assistant_what_the_rental_company_offers()
    {
        await NewTenantAsync("seo-llms");

        var llms = await app.CreateClient("seo-llms.test").GetStringAsync("/llms.txt");

        Assert.StartsWith("# Seo Verhuur\n\n> Reserveer online, wij leveren en zetten alles op in Mortsel en Edegem.\n", llms);
        Assert.Contains("Andere talen: [français](https://seo-llms.test/fr), [English](https://seo-llms.test/en)", llms);
        Assert.Contains("- Adres: Kerkstraat 1, 2640 Mortsel\n", llms);
        Assert.Contains("- Telefoon: +32470123456\n", llms);
        Assert.Contains("## Hier leveren we\n\nMortsel, Edegem\n", llms);
        Assert.Contains("### [Springkastelen](https://seo-llms.test/springkastelen)\n", llms);
        Assert.Contains("- [Piratenkasteel](https://seo-llms.test/huren/piraat): € 120,00 per dag (inclusief btw). Een kasteel met een mast\n", llms);
        Assert.Contains("## Pakketten\n\n- [Feestpakket](https://seo-llms.test/huren/feest): € 150,00 per dag (inclusief btw). Bevat: 1 × Molen\n", llms);
        Assert.Contains("## [Veelgestelde vragen](https://seo-llms.test/veelgestelde-vragen)\n\n### Leveren jullie bij mij thuis?\n", llms);
    }

    [Fact]
    public async Task One_rental_company_never_shows_up_in_the_files_of_another()
    {
        await NewTenantAsync("seo-eerste");
        await NewTenantAsync("seo-tweede", itemName: "Tweede kasteel");

        foreach (var file in new[] { "/robots.txt", "/sitemap.xml", "/llms.txt" })
        {
            var second = await app.CreateClient("seo-tweede.test").GetStringAsync(file);
            Assert.DoesNotContain("seo-eerste", second);
            Assert.DoesNotContain("Piratenkasteel", second);
        }
    }

    /// <summary>
    /// Een verhuurder met bedrijfsgegevens, twee categorieën (alleen springkastelen heeft artikelen), twee
    /// springkastelen, een molen zonder categorie en een feestpakket met de molen.
    /// </summary>
    private async Task NewTenantAsync(string slug, string itemName = "Piratenkasteel")
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == slug);
        tenant.Name = "Seo Verhuur";
        tenant.LogoUrl = "https://seo.test/logo.png";
        tenant.Street = "Kerkstraat 1";
        tenant.PostalCode = "2640";
        tenant.City = "Mortsel";
        tenant.ContactPhone = "+32470123456";
        tenant.ServiceArea = ["Mortsel", "Edegem"];
        tenant.FacebookUrl = "https://www.facebook.com/seo";
        var castles = new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] };
        var games = new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen")] };
        var pirate = new RentalItem
        {
            Slug = "piraat", Category = castles, Stock = 1, Pricing = new Pricing { DayPrice = 120 },
            Images = [new ItemImage { Url = "https://seo.test/piraat.jpg" }],
            Translations = [new Translation { Culture = "nl", Name = itemName, Description = "Een kasteel met een mast." }],
        };
        var princess = new RentalItem { Slug = "prinses", Category = castles, Stock = 1, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Prinsessenkasteel")] };
        var mill = new RentalItem { Slug = "molen", Stock = 1, Pricing = new Pricing { DayPrice = 30 }, Translations = [T("nl", "Molen")] };
        db.AddRange(castles, games, pirate, princess, mill);
        await db.SaveChangesAsync();
        db.Bundles.Add(new Bundle { Slug = "feest", Pricing = new Pricing { DayPrice = 150 }, Translations = [T("nl", "Feestpakket")], Items = [new BundleItem { RentalItemId = mill.Id }] });
        await db.SaveChangesAsync();
    }

    /// <summary>Het JSON-LD-object van dit type in de pagina.</summary>
    private static JsonElement JsonLd(string html, string type) =>
        JsonLdScript().Matches(html)
            .Select(m => JsonDocument.Parse(m.Groups[1].Value).RootElement)
            .Single(e => e.GetProperty("@type").GetString() == type);

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdScript();

    private async Task<string> PageAsync(string tenantSlug, string path) =>
        WebUtility.HtmlDecode(await app.CreateClient($"{tenantSlug}.test").GetStringAsync(path));

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
