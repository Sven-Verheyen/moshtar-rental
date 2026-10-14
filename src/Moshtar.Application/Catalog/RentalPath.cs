namespace Moshtar.Application.Catalog;

/// <summary>
/// Het pad van de pagina van een artikel of pakket op de reservatiewebsite (ADR 0004): het padwoord van de taal met de
/// slug, en een taalvoorvoegsel behalve in de standaardtaal, bv. "/huren/piraat" of "/fr/louer/piraat". Ook voor links
/// in mails, die buiten een request gemaakt worden.
/// </summary>
public static class RentalPath
{
    public static readonly IReadOnlyDictionary<string, string> Words =
        new Dictionary<string, string> { ["nl"] = "huren", ["fr"] = "louer", ["en"] = "rent" };

    public static string For(string slug, string culture, string defaultCulture) =>
        culture == defaultCulture ? $"/{Words[culture]}/{slug}" : $"/{culture}/{Words[culture]}/{slug}";
}
