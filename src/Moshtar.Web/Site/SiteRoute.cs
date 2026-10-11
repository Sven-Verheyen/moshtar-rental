namespace Moshtar.Web.Site;

/// <summary>
/// Een pagina van de reservatiewebsite, los van de taal. Elke pagina kent haar pad in elke taal,
/// zodat de taalkeuze en de hreflang-links naar dezelfde pagina in een andere taal wijzen (ADR 0004).
/// </summary>
public abstract record SiteRoute
{
    /// <summary>Het pad in deze taal, zonder taalvoorvoegsel en zonder schuine streep vooraan, bv. "louer/springkasteel".</summary>
    public abstract string PathIn(string culture);

    /// <summary>Herkent een pad zonder taalvoorvoegsel, in welke taal ook geschreven, of null als het geen pagina van de reservatiewebsite is.</summary>
    public static SiteRoute? Parse(string path)
    {
        var segments = path.Trim('/').Split('/');
        return segments switch
        {
            [""] => Home,
            [var word] when ContactRoute.Words.ContainsValue(word) => Contact,
            [var word] when FaqRoute.Words.ContainsValue(word) => Faq,
            [var word] when PrivacyRoute.Words.ContainsValue(word) => Privacy,
            [var word] when CartRoute.Words.ContainsValue(word) => Cart,
            [var word, var slug] when slug.Length > 0 && RentalRoute.Words.ContainsValue(word) => new RentalRoute(slug),
            _ => null,
        };
    }

    /// <summary>De home van de reservatiewebsite.</summary>
    public static readonly SiteRoute Home = new HomeRoute();

    /// <summary>De contactpagina.</summary>
    public static readonly SiteRoute Contact = new ContactRoute();

    /// <summary>De pagina met veelgestelde vragen.</summary>
    public static readonly SiteRoute Faq = new FaqRoute();

    /// <summary>De privacy- en cookieverklaring.</summary>
    public static readonly SiteRoute Privacy = new PrivacyRoute();

    /// <summary>De winkelkar van de klant.</summary>
    public static readonly SiteRoute Cart = new CartRoute();
}

/// <summary>De home: "/" in de standaardtaal, "/fr" of "/en" in de andere.</summary>
public sealed record HomeRoute : SiteRoute
{
    public override string PathIn(string culture) => "";
}

/// <summary>De pagina van een artikel of pakket, met één slug in alle talen.</summary>
public sealed record RentalRoute(string Slug) : SiteRoute
{
    internal static readonly Dictionary<string, string> Words = new() { ["nl"] = "huren", ["fr"] = "louer", ["en"] = "rent" };

    public override string PathIn(string culture) => $"{Words[culture]}/{Slug}";
}

/// <summary>
/// De pagina van een categorie, met een eigen slug in elke taal, bv. "/springkastelen" en "/fr/chateaux-gonflables".
/// Die slugs staan in de databank (websiteteksten); daarom herkent <see cref="SiteLanguage"/> deze pagina en niet <see cref="SiteRoute.Parse"/>.
/// </summary>
public sealed record CategoryRoute(Guid CategoryId, IReadOnlyDictionary<string, string> Slugs) : SiteRoute
{
    public override string PathIn(string culture) => Slugs[culture];
}

/// <summary>De contactpagina, met de bedrijfsgegevens en het werkgebied van de verhuurder.</summary>
public sealed record ContactRoute : SiteRoute
{
    internal static readonly Dictionary<string, string> Words = new() { ["nl"] = "contact", ["fr"] = "contact", ["en"] = "contact" };

    public override string PathIn(string culture) => Words[culture];
}

/// <summary>De pagina met veelgestelde vragen.</summary>
public sealed record FaqRoute : SiteRoute
{
    internal static readonly Dictionary<string, string> Words =
        new() { ["nl"] = "veelgestelde-vragen", ["fr"] = "questions-frequentes", ["en"] = "faq" };

    public override string PathIn(string culture) => Words[culture];
}

/// <summary>De privacy- en cookieverklaring van de verhuurder.</summary>
public sealed record PrivacyRoute : SiteRoute
{
    internal static readonly Dictionary<string, string> Words =
        new() { ["nl"] = "privacy", ["fr"] = "confidentialite", ["en"] = "privacy" };

    public override string PathIn(string culture) => Words[culture];
}

/// <summary>De winkelkar van de klant. Niet geïndexeerd: elke klant ziet er iets anders.</summary>
public sealed record CartRoute : SiteRoute
{
    internal static readonly Dictionary<string, string> Words = new() { ["nl"] = "winkelkar", ["fr"] = "panier", ["en"] = "cart" };

    public override string PathIn(string culture) => Words[culture];
}
