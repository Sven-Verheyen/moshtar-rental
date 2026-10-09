namespace Moshtar.Domain.Common;

/// <summary>Vertaalde tekst van een catalogusobject in één taal (bv. "nl", "fr", "en").</summary>
public class Translation
{
    public string Culture { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public static class TranslationExtensions
{
    /// <summary>Kiest de vertaling voor de gevraagde taal, anders de standaardtaal, anders de eerste.</summary>
    public static Translation? For(this IEnumerable<Translation> translations, string culture, string fallbackCulture)
    {
        var list = translations as IReadOnlyList<Translation> ?? translations.ToList();
        return list.FirstOrDefault(t => t.Culture == culture)
            ?? list.FirstOrDefault(t => t.Culture == fallbackCulture)
            ?? list.FirstOrDefault();
    }
}
