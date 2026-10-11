using Moshtar.Domain.Common;

namespace Moshtar.Domain.Website;

/// <summary>Een tekst van de reservatiewebsite die een verhuurder kan aanpassen.</summary>
public enum WebsiteTextKey
{
    HomeTitle,
    HomeSubtitle,
    /// <summary>De hele lijst veelgestelde vragen, als JSON.</summary>
    Faq,
    CategoryName,
    CategorySlug,
    CategoryIntro,
}

/// <summary>Een veelgestelde vraag met haar antwoord.</summary>
public sealed record FaqEntry(string Question, string Answer);

/// <summary>
/// Wat een verhuurder zelf aanpaste aan één tekst van zijn reservatiewebsite, in één taal (en voor één categorie bij
/// de categorieteksten). Zonder aanpassing geldt de standaardtekst van Moshtar; net als bij de mailsjablonen worden
/// alleen aanpassingen bewaard.
/// </summary>
public class WebsiteTextCustomization : TenantEntity
{
    public WebsiteTextKey Key { get; set; }
    public string Culture { get; set; } = "";
    /// <summary>De categorie bij <see cref="WebsiteTextKey.CategoryName"/>, <see cref="WebsiteTextKey.CategorySlug"/> en <see cref="WebsiteTextKey.CategoryIntro"/>.</summary>
    public Guid? CategoryId { get; set; }
    public string Value { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    /// <summary>De gebruiker die de tekst laatst aanpaste.</summary>
    public Guid UpdatedByUserId { get; set; }
}
