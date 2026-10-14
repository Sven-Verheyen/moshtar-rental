using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Domain.Availability;

/// <summary>
/// Zet reservatielijnen om naar bezetting per artikel; pakketten worden opengeklapt, met de artikelen die voor de
/// reservatie binnen het pakket vervangen zijn.
/// </summary>
public static class DemandExpander
{
    public static IEnumerable<StockDemand> Expand(
        IEnumerable<ReservationLine> lines,
        DateRange period,
        IReadOnlyDictionary<Guid, Bundle> bundles) =>
        ExpandLines(lines, period, bundles).Select(d => d.Demand);

    /// <summary>Wat een reservatie bezet, per artikel, met het pakket waarin het artikel zit (of null voor een losse lijn).</summary>
    public static IEnumerable<ReservedDemand> Expand(Reservation reservation, IReadOnlyDictionary<Guid, Bundle> bundles) =>
        ExpandLines(reservation.Lines, reservation.Period, bundles)
            .Select(d => new ReservedDemand(reservation.Id, d.Demand, d.BundleId));

    /// <summary>
    /// Hoeveel exemplaren van een artikel een reservatie nodig heeft: los en in haar gereserveerde pakketten samen. Zoveel
    /// moet een alternatief vrij hebben om het te vervangen.
    /// </summary>
    public static int QuantityOf(Reservation reservation, Guid rentalItemId, IReadOnlyDictionary<Guid, Bundle> bundles) =>
        ExpandLines(reservation.Lines, reservation.Period, bundles).Where(d => d.Demand.RentalItemId == rentalItemId).Sum(d => d.Demand.Quantity);

    private static IEnumerable<(StockDemand Demand, Guid? BundleId)> ExpandLines(
        IEnumerable<ReservationLine> lines,
        DateRange period,
        IReadOnlyDictionary<Guid, Bundle> bundles)
    {
        foreach (var line in lines)
        {
            if (line.RentalItemId is { } itemId)
            {
                yield return (new StockDemand(itemId, line.Quantity, period), null);
            }
            else if (line.BundleId is { } bundleId)
            {
                if (!bundles.TryGetValue(bundleId, out var bundle))
                    throw new InvalidOperationException($"Pakket {bundleId} niet gevonden.");
                // Een artikel dat voor deze reservatie binnen het pakket vervangen is, bezet het vervangende.
                foreach (var bundleItem in bundle.Items)
                    yield return (new StockDemand(line.ItemInBundle(bundleItem.RentalItemId), bundleItem.Quantity * line.Quantity, period), bundleId);
            }
        }
    }
}
