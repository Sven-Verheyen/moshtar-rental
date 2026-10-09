namespace Moshtar.Domain.Catalog;

/// <summary>
/// Prijs voor een huurperiode: de eerste dag aan dagprijs, elke extra dag aan de
/// (meestal lagere) prijs per extra dag. Prijzen zijn inclusief btw.
/// </summary>
public class Pricing
{
    public decimal DayPrice { get; set; }
    public decimal? ExtraDayPrice { get; set; }

    public decimal PriceFor(int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days));
        return DayPrice + (days - 1) * (ExtraDayPrice ?? DayPrice);
    }
}
