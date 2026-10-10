using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Website;

/// <summary>
/// De slug van een categorie in de URL van de reservatiewebsite, per taal (ADR 0004): kleine letters, cijfers en
/// koppeltekens, bv. "springkastelen" of "chateaux-gonflables".
/// </summary>
public static partial class CategorySlug
{
    /// <summary>
    /// Woorden die al een vaste betekenis hebben in de URL (taalvoorvoegsels, padwoorden van de reservatiewebsite,
    /// het back office) en dus geen categorie kunnen zijn.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>
    {
        "nl", "fr", "en", "admin", "huren", "louer", "rent", "contact",
        "veelgestelde-vragen", "questions-frequentes", "faq", "not-found", "error",
    };

    public const int MaxLength = 100;

    /// <summary>Maakt een slug van een naam: "Châteaux gonflables" wordt "chateaux-gonflables".</summary>
    public static string From(string name)
    {
        var withoutAccents = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                withoutAccents.Append(c);
        var slug = NotSlug().Replace(withoutAccents.ToString().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    /// <summary>Wat er mis is met deze slug, of null als ze goed is.</summary>
    public static string? Problem(string slug) =>
        slug.Length == 0 ? "De slug is leeg."
        : slug.Length > MaxLength ? $"De slug is langer dan {MaxLength} tekens."
        : !Valid().IsMatch(slug) ? $"De slug \"{slug}\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. springkastelen."
        : Reserved.Contains(slug) ? $"\"{slug}\" heeft al een andere betekenis op je reservatiewebsite; kies een andere slug."
        : null;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Valid();
}
