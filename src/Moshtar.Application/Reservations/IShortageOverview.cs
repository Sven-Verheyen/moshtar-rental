using Moshtar.Domain.Common;

namespace Moshtar.Application.Reservations;

/// <summary>Een dag met een tekort: meer gereserveerd (bufferdagen inbegrepen) dan verhuurbaar.</summary>
public sealed record ShortageDay(DateOnly Day, int Rentable, int Reserved)
{
    /// <summary>Hoeveel exemplaren er te weinig zijn.</summary>
    public int Missing => Reserved - Rentable;
}

/// <summary>
/// Een reservatie die door een tekort van een artikel getroffen is: ze gebruikt het artikel op
/// <see cref="ShortageDays"/>, <see cref="Quantity"/> keer (het meeste op één van die dagen).
/// <see cref="BundleName"/> is het gereserveerde pakket waarin het artikel zit, of null voor een los artikel.
/// </summary>
public sealed record ShortageReservation(
    Guid ReservationId,
    string Number,
    string CustomerName,
    DateRange Period,
    int Quantity,
    string? BundleName,
    IReadOnlyList<DateOnly> ShortageDays);

/// <summary>De tekorten van één artikel: de dagen en alle getroffen reservaties, op begindatum.</summary>
public sealed record ItemShortages(
    Guid RentalItemId,
    string RentalItemName,
    IReadOnlyList<ShortageDay> Days,
    IReadOnlyList<ShortageReservation> Reservations);

/// <summary>
/// De lijst Tekorten in het back office, voor Beheerder en Medewerker: per artikel de dagen vanaf vandaag waarop meer
/// gereserveerd is dan verhuurbaar, door een blokkering of een lagere voorraad. Berekend, niet bewaard: een tekort
/// verdwijnt vanzelf zodra het opgelost is (annuleren, blokkering inkorten, voorraad verhogen).
/// </summary>
public interface IShortageOverview
{
    /// <summary>De artikelen met een tekort, op naam.</summary>
    Task<IReadOnlyList<ItemShortages>> GetAsync(CancellationToken ct = default);

    /// <summary>De teller in het menu: hoeveel keer een reservatie getroffen is door een tekort (per artikel).</summary>
    Task<int> CountAsync(CancellationToken ct = default);
}
