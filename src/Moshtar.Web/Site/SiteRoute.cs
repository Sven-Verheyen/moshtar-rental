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
            [var word, var slug] when slug.Length > 0 && RentalRoute.Words.ContainsValue(word) => new RentalRoute(slug),
            _ => null,
        };
    }

    /// <summary>De home van de reservatiewebsite.</summary>
    public static readonly SiteRoute Home = new HomeRoute();

    /// <summary>De contactpagina.</summary>
    public static readonly SiteRoute Contact = new ContactRoute();
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

/// <summary>De contactpagina, met de bedrijfsgegevens en het werkgebied van de verhuurder.</summary>
public sealed record ContactRoute : SiteRoute
{
    internal static readonly Dictionary<string, string> Words = new() { ["nl"] = "contact", ["fr"] = "contact", ["en"] = "contact" };

    public override string PathIn(string culture) => Words[culture];
}
