using Moshtar.Application.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De naam en beschrijving per taal van een artikel of pakket, zoals de beheerder ze ingeeft: alleen de naam in de
/// standaardtaal is verplicht; een taal zonder naam toont op de reservatiewebsite die van de standaardtaal.
/// </summary>
internal static class CatalogTexts
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 4000;

    /// <summary>
    /// De vertalingen (alleen talen met een naam) uit wat de beheerder ingaf; wat er mis is, komt bij
    /// <paramref name="problems"/>.
    /// </summary>
    public static List<Translation> Validate(Tenant tenant, IReadOnlyList<RentalItemText> texts, List<string> problems)
    {
        foreach (var text in texts.Where(t => !tenant.Cultures.Contains(t.Culture)))
            problems.Add($"Je biedt de taal '{text.Culture}' niet aan.");

        var translations = new List<Translation>();
        foreach (var culture in tenant.Cultures)
        {
            var text = texts.FirstOrDefault(t => t.Culture == culture);
            var name = text?.Name.Trim() ?? "";
            var description = text?.Description?.Trim().ReplaceLineEndings("\n") is { Length: > 0 } d ? d : null;
            if (name.Length == 0)
            {
                if (culture == tenant.DefaultCulture) problems.Add($"De naam in het {LanguageName(culture)} is leeg.");
                else if (description is not null) problems.Add($"Vul ook de naam in het {LanguageName(culture)} in, of laat de beschrijving leeg.");
                continue;
            }
            if (name.Length > MaxNameLength) problems.Add($"De naam in het {LanguageName(culture)} is langer dan {MaxNameLength} tekens.");
            if (description?.Length > MaxDescriptionLength) problems.Add($"De beschrijving in het {LanguageName(culture)} is langer dan {MaxDescriptionLength} tekens.");
            translations.Add(new Translation { Culture = culture, Name = name, Description = description });
        }
        return translations;
    }

    /// <summary>De teksten in elke taal van de verhuurder, leeg waar er nog geen vertaling is.</summary>
    public static IReadOnlyList<RentalItemText> ForEditing(Tenant tenant, IReadOnlyList<Translation> translations) =>
        tenant.Cultures
            .Select(culture => translations.FirstOrDefault(t => t.Culture == culture) is { } t
                ? new RentalItemText(culture, t.Name, t.Description)
                : new RentalItemText(culture, "", null))
            .ToList();

    /// <summary>
    /// De talen (buiten de standaardtaal) waarin de naam ontbreekt, of de beschrijving terwijl de standaardtaal er een heeft.
    /// </summary>
    public static IReadOnlyList<string> Missing(Tenant tenant, IReadOnlyList<Translation> translations)
    {
        var standard = translations.FirstOrDefault(t => t.Culture == tenant.DefaultCulture);
        return tenant.Cultures.Where(culture => culture != tenant.DefaultCulture)
            .Where(culture => translations.FirstOrDefault(t => t.Culture == culture) is not { } t
                || (standard?.Description is not null && t.Description is null))
            .ToList();
    }

    public static string LanguageName(string culture) => culture switch
    {
        "nl" => "Nederlands",
        "fr" => "Frans",
        "en" => "Engels",
        _ => culture,
    };
}
