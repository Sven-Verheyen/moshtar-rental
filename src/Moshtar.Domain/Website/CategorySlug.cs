using Moshtar.Domain.Common;

namespace Moshtar.Domain.Website;

/// <summary>
/// De slug van een categorie in de URL van de reservatiewebsite, per taal (ADR 0004): kleine letters, cijfers en
/// koppeltekens, bv. "springkastelen" of "chateaux-gonflables".
/// </summary>
public static class CategorySlug
{
    /// <summary>
    /// Woorden die al een vaste betekenis hebben in de URL (taalvoorvoegsels, padwoorden van de reservatiewebsite,
    /// het back office) en dus geen categorie kunnen zijn.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>
    {
        "nl", "fr", "en", "admin", "huren", "louer", "rent", "contact",
        "veelgestelde-vragen", "questions-frequentes", "faq", "privacy", "confidentialite", "toestemming",
        "winkelkar", "panier", "cart",
        "not-found", "error",
    };

    public const int MaxLength = SlugText.MaxLength;

    /// <summary>Maakt een slug van een naam: "Châteaux gonflables" wordt "chateaux-gonflables".</summary>
    public static string From(string name) => SlugText.From(name);

    /// <summary>Wat er mis is met deze slug, of null als ze goed is.</summary>
    public static string? Problem(string slug) =>
        SlugText.Problem(slug, "springkastelen")
        ?? (Reserved.Contains(slug) ? $"\"{slug}\" heeft al een andere betekenis op je reservatiewebsite; kies een andere slug." : null);
}
