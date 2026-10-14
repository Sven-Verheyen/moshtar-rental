using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

/// <summary>
/// De slug van een artikel: één in alle talen, achter het padwoord van de taal (bv. "/huren/springkasteel-jungle",
/// ADR 0004). Uniek bij de verhuurder, onder de artikelen en pakketten samen: ze delen dat padwoord.
/// </summary>
public static class RentalItemSlug
{
    public const int MaxLength = SlugText.MaxLength;

    /// <summary>Maakt een slug van een naam: "Château Pirate" wordt "chateau-pirate".</summary>
    public static string From(string name) => SlugText.From(name);

    /// <summary>Wat er mis is met deze slug, of null als ze goed is.</summary>
    public static string? Problem(string slug) => SlugText.Problem(slug, "springkasteel-jungle");

    /// <summary>Een slug die nog vrij is: "jungle", anders "jungle-2", "jungle-3", ...</summary>
    public static string Unique(string slug, IReadOnlyCollection<string> taken) => SlugText.Unique(slug, taken);
}
