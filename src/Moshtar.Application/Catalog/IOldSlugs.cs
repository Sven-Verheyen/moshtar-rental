using Moshtar.Domain.Catalog;

namespace Moshtar.Application.Catalog;

/// <summary>
/// De oude slugs van de verhuurder van het huidige request, zodat de reservatiewebsite een oud adres met een 301
/// doorstuurt naar de huidige pagina. Gedeeld door categorieën, artikelen en pakketten.
/// </summary>
public interface IOldSlugs
{
    /// <summary>Waar een oude slug van deze soort in deze taal nu naar wijst, of null als ze geen oude slug is.</summary>
    Task<Guid?> FindAsync(SlugKind kind, string culture, string slug, CancellationToken ct = default);
}
