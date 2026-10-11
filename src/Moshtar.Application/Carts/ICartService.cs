using Moshtar.Application.Catalog;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;

namespace Moshtar.Application.Carts;

/// <summary>
/// De winkelkar van een klant op de reservatiewebsite, terug te vinden met de sleutel uit zijn cookie.
/// Elke wijziging geeft de sleutel terug, ook als er net een nieuwe winkelkar gemaakt werd.
/// </summary>
public interface ICartService
{
    /// <summary>De winkelkar met prijzen en beschikbaarheid voor zijn huurperiode, in deze taal, of null als er geen is.</summary>
    Task<CartView?> GetAsync(string? key, string culture, CancellationToken ct = default);

    /// <summary>Het aantal stuks in de winkelkar, voor het icoon in de kop; 0 zonder winkelkar.</summary>
    Task<int> CountAsync(string? key, CancellationToken ct = default);

    /// <summary>
    /// Legt een artikel of pakket in de winkelkar voor deze huurperiode, die dan voor de hele winkelkar geldt.
    /// Lukt niet als het, samen met wat al in de winkelkar zit, niet vrij is; dan verandert er niets.
    /// </summary>
    Task<CartChange> AddAsync(string? key, CatalogRef entry, int quantity, DateRange period, CancellationToken ct = default);

    /// <summary>Wijzigt het aantal van een lijn; 0 haalt ze uit de winkelkar.</summary>
    Task<CartChange> SetQuantityAsync(string? key, CatalogRef entry, int quantity, CancellationToken ct = default);

    /// <summary>Een andere huurperiode voor alles in de winkelkar; wat dan niet meer vrij is, toont de winkelkar.</summary>
    Task<CartChange> ChangePeriodAsync(string? key, DateRange period, CancellationToken ct = default);
}

/// <summary>Het resultaat van een wijziging: de sleutel van de winkelkar (null als er geen is) en of het lukte.</summary>
public record CartChange(string? Key, bool Succeeded);

/// <summary>De winkelkar zoals de klant hem ziet.</summary>
public record CartView(DateRange Period, IReadOnlyList<CartLineView> Lines)
{
    public int Count => Lines.Sum(l => l.Quantity);

    /// <summary>Het totaal van wat nog te reserveren is: zonder lijnen die niet meer bestaan.</summary>
    public decimal Total => Lines.Where(l => l.Entry is not null).Sum(l => l.LineTotal);

    public bool AllAvailable => Lines.All(l => l.Available);
}

/// <summary>
/// Een lijn in de winkelkar met haar prijs voor de huurperiode. <see cref="Entry"/> is null als het artikel of pakket
/// intussen niet meer op de reservatiewebsite staat; <see cref="Available"/> zegt of het in de huurperiode nog vrij is.
/// </summary>
public record CartLineView(CatalogRef Ref, CatalogEntry? Entry, int Quantity, decimal UnitPrice, bool Available)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
