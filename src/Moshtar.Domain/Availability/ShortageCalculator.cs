namespace Moshtar.Domain.Availability;

/// <summary>Wat één reservatie van één artikel bezet, zonder bufferdagen.</summary>
public readonly record struct ReservedDemand(Guid ReservationId, StockDemand Demand);

/// <summary>
/// Een tekort: op deze dag zijn van dit artikel meer exemplaren gereserveerd (bufferdagen inbegrepen) dan er
/// verhuurbaar zijn (voorraad min blokkeringen). <see cref="ReservationIds"/> zijn alle reservaties die het artikel op
/// die dag gebruiken.
/// </summary>
public sealed record Shortage(Guid RentalItemId, DateOnly Day, int Rentable, int Reserved, IReadOnlyList<Guid> ReservationIds)
{
    /// <summary>Hoeveel exemplaren er te weinig zijn.</summary>
    public int Missing => Reserved - Rentable;
}

/// <summary>
/// Zoekt de tekorten: per artikel en per dag vanaf vandaag, wat gereserveerd is tegenover wat verhuurbaar is.
/// Berekend, niet bewaard: een tekort verdwijnt vanzelf zodra er niet meer gereserveerd is dan verhuurbaar.
/// Reservaties tellen met de bufferdagen van de verhuurder, blokkeringen zonder, net als in <see cref="AvailabilityCalculator"/>.
/// </summary>
public static class ShortageCalculator
{
    /// <param name="stockPerItem">De voorraad per artikel; een artikel dat ontbreekt, heeft er geen.</param>
    /// <param name="reserved">Wat bevestigde reservaties bezetten, met pakketten al opengeklapt naar hun artikelen.</param>
    /// <param name="blocked">De blokkeringen.</param>
    /// <param name="from">Vandaag: eerdere dagen tellen niet meer mee.</param>
    public static IReadOnlyList<Shortage> Find(
        IReadOnlyDictionary<Guid, int> stockPerItem,
        IEnumerable<ReservedDemand> reserved,
        IEnumerable<StockDemand> blocked,
        DateOnly from,
        int bufferDaysBefore = 0,
        int bufferDaysAfter = 0)
    {
        var reservedPerItem = reserved
            .Select(r => r with { Demand = r.Demand with { Period = r.Demand.Period.Expand(bufferDaysBefore, bufferDaysAfter) } })
            .Where(r => r.Demand.Period.End >= from)
            .GroupBy(r => r.Demand.RentalItemId);
        var blockedPerItem = blocked.ToLookup(b => b.RentalItemId);

        var shortages = new List<Shortage>();
        foreach (var item in reservedPerItem.OrderBy(g => g.Key))
        {
            var stock = stockPerItem.GetValueOrDefault(item.Key);
            var days = item.SelectMany(r => r.Demand.Period.EachDay()).Where(day => day >= from).Distinct().Order();
            foreach (var day in days)
            {
                var onDay = item.Where(r => r.Demand.Period.Start <= day && day <= r.Demand.Period.End).ToList();
                var reservedOnDay = onDay.Sum(r => r.Demand.Quantity);
                var rentable = Math.Max(0, stock - blockedPerItem[item.Key]
                    .Where(b => b.Period.Start <= day && day <= b.Period.End).Sum(b => b.Quantity));
                if (reservedOnDay > rentable)
                    shortages.Add(new Shortage(item.Key, day, rentable, reservedOnDay,
                        onDay.Select(r => r.ReservationId).Distinct().ToList()));
            }
        }
        return shortages;
    }
}
