using Moshtar.Domain.Website;

namespace Moshtar.Application.Website;

/// <summary>Een tekst zoals hij geldt: de aanpassing van de verhuurder, of de standaardtekst.</summary>
public sealed record WebsiteText(string Value, bool IsCustomized);

/// <summary>De naam, slug en inleiding van een categorie in één taal.</summary>
public sealed record CategoryTexts(Guid CategoryId, WebsiteText Name, WebsiteText Slug, WebsiteText Intro);

/// <summary>Alle aanpasbare teksten van de reservatiewebsite in één taal.</summary>
public sealed record WebsiteTexts(
    string Culture,
    WebsiteText HomeTitle,
    WebsiteText HomeSubtitle,
    IReadOnlyList<FaqEntry> Faq,
    bool FaqIsCustomized,
    IReadOnlyList<CategoryTexts> Categories);

/// <summary>Een tekst kan zo niet bewaard worden; de boodschap is voor de beheerder bedoeld.</summary>
public sealed class WebsiteTextException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>De teksten van de reservatiewebsite van de verhuurder van het huidige request.</summary>
public interface IWebsiteTexts
{
    /// <summary>De teksten in deze taal, met de standaardtekst waar de verhuurder niets aanpaste.</summary>
    Task<WebsiteTexts> GetAsync(string culture, CancellationToken ct = default);
}

/// <summary>
/// De teksten van de reservatiewebsite beheren, in het back office. Alleen aanpassingen worden bewaard; een tekst
/// gelijk aan de standaardtekst is geen aanpassing. Elke methode gooit een <see cref="WebsiteTextException"/> als er
/// iets mis is, en bewaart dan niets.
/// </summary>
public interface IWebsiteTextAdministration : IWebsiteTexts
{
    Task SaveHomeAsync(string culture, string title, string subtitle, Guid userId, CancellationToken ct = default);

    /// <summary>Bewaart naam, slug en inleiding; de slug moet uniek zijn onder de categorieën in deze taal.</summary>
    Task SaveCategoryAsync(Guid categoryId, string culture, string name, string slug, string intro, Guid userId, CancellationToken ct = default);

    /// <summary>Bewaart de veelgestelde vragen in deze volgorde. Een lege lijst betekent: geen veelgestelde vragen.</summary>
    Task SaveFaqAsync(string culture, IReadOnlyList<FaqEntry> faq, Guid userId, CancellationToken ct = default);

    /// <summary>Verwijdert de aanpassing van één tekst in deze taal, zodat de standaardtekst weer geldt.</summary>
    Task ResetAsync(string culture, WebsiteTextKey key, Guid? categoryId = null, CancellationToken ct = default);
}
