using Moshtar.Application.Catalog;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;

namespace Moshtar.Application.Prices;

/// <summary>
/// De rekentool op de pagina Prijsregels: rekent een voorbeeldreservatie door met precies de prijsberekening van de
/// winkelkar (<see cref="PriceQuoter"/>), met de prijsregels die aan staan en een gekozen reservatiedatum. Bewaart niets;
/// beschikbaarheid speelt geen rol. Met een eigen, kortlevende context: de tabbladen van de pagina gebruiken de gedeelde.
/// </summary>
public interface IPriceExamples
{
    /// <summary>Wat de beheerder kan kiezen: de artikelen en pakketten van de reservatiewebsite, en vandaag.</summary>
    Task<PriceExampleChoices> GetChoicesAsync(CancellationToken ct = default);

    /// <summary>De prijzen van de voorbeeldreservatie; een artikel of pakket dat niet (meer) op de reservatiewebsite staat, valt weg.</summary>
    Task<PriceExampleResult> CalculateAsync(PriceExample example, CancellationToken ct = default);
}

/// <summary>De artikelen en pakketten in de standaardtaal, in de volgorde van het aanbod, en vandaag (klok van de verhuurder).</summary>
public sealed record PriceExampleChoices(DateOnly Today, IReadOnlyList<CatalogEntry> Entries);

/// <summary>Een voorbeeldreservatie: de huurperiode, de dag waarop gereserveerd wordt en de artikelen en pakketten met aantal.</summary>
public sealed record PriceExample(DateRange Period, DateOnly ReservationDate, IReadOnlyList<PriceExampleLine> Lines);

public sealed record PriceExampleLine(CatalogRef Entry, int Quantity);

/// <summary>De uitkomst van de rekentool: de lijnen, de kortingslijnen en wat de klant betaalt.</summary>
public sealed record PriceExampleResult(IReadOnlyList<PriceExampleLineResult> Lines, IReadOnlyList<PriceExampleDiscount> Discounts, decimal Subtotal, decimal Total);

/// <summary>
/// Een lijn voor het artikel of pakket <see cref="Entry"/>: de rekentool zoekt de lijn op die sleutel, want een artikel
/// of pakket dat niet meer op de reservatiewebsite staat, valt weg. <see cref="NormalPrice"/> is de gewone prijs per stuk
/// (zonder prijsregels), <see cref="UnitPrice"/> de prijs per stuk met de periodeprijzen van elke dag
/// (<see cref="Days"/>), en per stuk de korting die het kreeg.
/// </summary>
public sealed record PriceExampleLineResult(
    CatalogRef Entry, string Name, int Quantity, decimal NormalPrice, decimal UnitPrice, IReadOnlyList<PricedDay> Days, IReadOnlyList<PriceExamplePiece> Pieces)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>Eén stuk: zijn prijs en de korting die het kreeg, met de naam van de regel (of null en 0: geen korting).</summary>
public sealed record PriceExamplePiece(decimal Price, string? Rule, decimal Discount);

/// <summary>Een kortingslijn: de naam van de regel, wat ze samen aftrekt (positief) en over hoeveel stuks.</summary>
public sealed record PriceExampleDiscount(string Rule, decimal Amount, int Pieces);
