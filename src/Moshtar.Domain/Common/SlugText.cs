using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Common;

/// <summary>
/// De vorm van een slug in de URL van de reservatiewebsite, voor categorieën, artikelen en pakketten: kleine letters,
/// cijfers en koppeltekens, bv. "springkasteel-jungle".
/// </summary>
public static partial class SlugText
{
    public const int MaxLength = 100;

    /// <summary>Maakt een slug van een naam: "Château Pirate" wordt "chateau-pirate".</summary>
    public static string From(string name)
    {
        var withoutAccents = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                withoutAccents.Append(c);
        var slug = NotSlug().Replace(withoutAccents.ToString().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    /// <summary>Wat er mis is met de vorm van deze slug, of null als ze goed is.</summary>
    /// <param name="example">Een goede slug als voorbeeld in de boodschap.</param>
    public static string? Problem(string slug, string example) =>
        slug.Length == 0 ? "De slug is leeg."
        : slug.Length > MaxLength ? $"De slug is langer dan {MaxLength} tekens."
        : !Valid().IsMatch(slug) ? $"De slug \"{slug}\" mag alleen kleine letters, cijfers en koppeltekens bevatten, bv. {example}."
        : null;

    /// <summary>Een slug die nog vrij is: "jungle", anders "jungle-2", "jungle-3", ...</summary>
    public static string Unique(string slug, IReadOnlyCollection<string> taken)
    {
        var unique = slug;
        for (var n = 2; taken.Contains(unique); n++) unique = $"{slug}-{n}";
        return unique;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Valid();
}
