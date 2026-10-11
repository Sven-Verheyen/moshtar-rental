namespace Moshtar.Application.Carts;

/// <summary>
/// Het onderhoud van de winkelkarren van de huidige verhuurder, op de achtergrond: één herinneringsmail voor wie
/// erom vroeg en na 24 uur niet reserveerde, en winkelkarren wissen die 7 dagen niet gewijzigd en niet gereserveerd werden.
/// </summary>
public interface ICartHousekeeping
{
    Task<CartHousekeepingResult> RunAsync(DateTime nowUtc, CancellationToken ct = default);
}

public record CartHousekeepingResult(int RemindersSent, int CartsDeleted);
