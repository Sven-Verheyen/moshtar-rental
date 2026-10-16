using Moshtar.Application.Catalog;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;

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

    /// <summary>Stap 1 van het reserveren: het e-mailadres, de taal en de twee toestemmingen van de klant.</summary>
    Task<CartChange> IdentifyAsync(string? key, string email, string culture, bool wantsReminder, bool wantsNews, CancellationToken ct = default);

    /// <summary>
    /// Stap 2: reserveert alles uit de winkelkar in één reservatie, met de bestaande reserveerflow (automatisch
    /// bevestigd als de voorraad het toelaat, één bevestigingsmail). Daarna is de winkelkar afgesloten.
    /// </summary>
    Task<CartReservationResult> ReserveAsync(string? key, CartCustomerDetails details, CancellationToken ct = default);

    /// <summary>
    /// De winkelkar achter de link in de herinneringsmail: zijn sleutel voor de cookie en de taal van de klant,
    /// of null als hij intussen gereserveerd of gewist is.
    /// </summary>
    Task<CartReminderTarget?> FindByReminderKeyAsync(string reminderKey, CancellationToken ct = default);
}

public record CartReminderTarget(string Key, string? Culture);

/// <summary>
/// De gegevens van stap 2; het e-mailadres staat al op de winkelkar. Voor een zakelijke klant zijn voornaam en
/// achternaam die van de contactpersoon. <see cref="BillingAddress"/> null betekent "Factuuradres is hetzelfde".
/// </summary>
public record CartCustomerDetails(
    string FirstName,
    string LastName,
    string? Phone,
    Address Address,
    DeliveryMethod DeliveryMethod,
    string? Notes,
    string Culture,
    CustomerKind Kind = CustomerKind.Private,
    string? CompanyName = null,
    string? VatNumber = null,
    Address? BillingAddress = null);

/// <summary>
/// De reservatie als het lukte. Anders de artikelen waarvan intussen te weinig vrij is, of de resourcesleutel van
/// een andere melding (geen winkelkar, geen e-mailadres, geen of een ongeldig telefoonnummer, geen bedrijfsnaam of een
/// ongeldig btw-nummer voor een zakelijke klant, een onvolledig factuuradres, iets wat niet meer te huren is, een datum
/// in het verleden).
/// </summary>
public record CartReservationResult(Reservation? Reservation, IReadOnlySet<Guid> UnavailableItemIds, string? Problem)
{
    public bool Succeeded => Reservation is not null;

    public static CartReservationResult Refused(string problem) => new(null, new HashSet<Guid>(), problem);
}

/// <summary>Het resultaat van een wijziging: de sleutel van de winkelkar (null als er geen is) en of het lukte.</summary>
public record CartChange(string? Key, bool Succeeded);

/// <summary>
/// De winkelkar zoals de klant hem ziet, met de prijzen en kortingen volgens de prijsregels van vandaag
/// (<see cref="Prices.IPriceQuotes"/>): elke korting is een eigen lijn met de naam van haar regel.
/// </summary>
public record CartView(DateRange Period, IReadOnlyList<CartLineView> Lines, string? Email = null, bool WantsReminder = false, bool WantsNews = false,
    IReadOnlyList<CartDiscountView>? Discounts = null)
{
    public int Count => Lines.Sum(l => l.Quantity);

    /// <summary>De kortingslijnen; leeg zonder korting.</summary>
    public IReadOnlyList<CartDiscountView> DiscountLines => Discounts ?? [];

    /// <summary>Het totaal van wat nog te reserveren is, met de kortingen: zonder lijnen die niet meer bestaan.</summary>
    public decimal Total => Lines.Where(l => l.Entry is not null).Sum(l => l.LineTotal) - DiscountLines.Sum(d => d.Amount);

    public bool AllAvailable => Lines.All(l => l.Available);
}

/// <summary>
/// Een lijn in de winkelkar met haar prijs voor de huurperiode. <see cref="Entry"/> is null als het artikel of pakket
/// intussen niet meer op de reservatiewebsite staat; <see cref="Available"/> zegt of het in de huurperiode nog vrij is.
/// </summary>
public record CartLineView(CatalogRef Ref, CatalogEntry? Entry, int Quantity, decimal UnitPrice, bool Available)
{
    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>Wat de kortingen van de stuks op deze lijn samen aftrekken (positief), uit de berekening per stuk; voor GA4.</summary>
    public decimal Discount { get; init; }
}

/// <summary>Een kortingslijn in de winkelkar: de naam van de prijsregel in de taal van de klant en het bedrag (positief).</summary>
public record CartDiscountView(string Name, decimal Amount);
