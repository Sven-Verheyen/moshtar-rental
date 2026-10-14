using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Domain.Availability;

/// <summary>Zet reservatielijnen om naar bezetting per artikel; pakketten worden opengeklapt.</summary>
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
                foreach (var bundleItem in bundle.Items)
                    yield return (new StockDemand(bundleItem.RentalItemId, bundleItem.Quantity * line.Quantity, period), bundleId);
            }
        }
    }
}
