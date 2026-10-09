namespace Moshtar.Domain.Availability;

/// <summary>
/// Berekent of een gevraagde boeking past binnen de voorraad.
/// Bestaande bezetting wordt per dag opgeteld; reservaties worden daarbij uitgebreid met de
/// bufferdagen van de tenant (leveren, ophalen, poetsen). Blokkeringen tellen zonder buffer.
/// </summary>
public static class AvailabilityCalculator
{
    public static IReadOnlyList<StockShortage> FindShortages(
        IReadOnlyDictionary<Guid, int> stockPerItem,
        IEnumerable<StockDemand> reserved,
        IEnumerable<StockDemand> blocked,
        IEnumerable<StockDemand> requested,
        int bufferDaysBefore = 0,
        int bufferDaysAfter = 0)
    {
        // Vraag van de nieuwe boeking, ook met buffer: twee verhuringen mogen elkaars buffer niet raken.
        var requestedPerItem = requested
            .Select(d => d with { Period = d.Period.Expand(bufferDaysBefore, bufferDaysAfter) })
            .GroupBy(d => d.RentalItemId)
            .ToList();
        if (requestedPerItem.Count == 0) return [];

        var relevantItems = requestedPerItem.Select(g => g.Key).ToHashSet();
        var occupied = reserved
            .Where(d => relevantItems.Contains(d.RentalItemId))
            .Select(d => d with { Period = d.Period.Expand(bufferDaysBefore, bufferDaysAfter) })
            .Concat(blocked.Where(d => relevantItems.Contains(d.RentalItemId)))
            .ToList();

        var shortages = new List<StockShortage>();
        foreach (var group in requestedPerItem)
        {
            var stock = stockPerItem.GetValueOrDefault(group.Key);
            var days = group.SelectMany(d => d.Period.EachDay()).Distinct().Order();
            foreach (var day in days)
            {
                var required =
                    group.Where(d => d.Period.Start <= day && day <= d.Period.End).Sum(d => d.Quantity) +
                    occupied.Where(d => d.RentalItemId == group.Key && d.Period.Start <= day && day <= d.Period.End).Sum(d => d.Quantity);
                if (required > stock)
                    shortages.Add(new StockShortage(group.Key, day, stock, required));
            }
        }
        return shortages;
    }
}
