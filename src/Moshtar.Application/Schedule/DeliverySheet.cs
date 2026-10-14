using Moshtar.Domain.Common;
using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De Leverlijst: het printblad van één ronde. <see cref="Stops"/> staan in de volgorde van de ronde, of op postcode
/// zolang er geen route is (<see cref="Round"/> null). <see cref="LoadingList"/> is de laadlijst: hoeveel van elk
/// artikel mee moet, met de pakketten opengeklapt in hun artikelen. <see cref="BusinessPoint"/> is het bedrijfsadres
/// op de kaart, waar de ronde vertrekt en terugkomt.
/// </summary>
public record DeliverySheet(
    DateOnly Day,
    RoundKind Kind,
    ScheduleRound? Round,
    IReadOnlyList<ScheduleStop> Stops,
    IReadOnlyList<LoadingItem> LoadingList,
    GeoPoint? BusinessPoint)
{
    /// <summary>De leverlijst van de leverronde of de ophaalronde van een dag in de Planning.</summary>
    public static DeliverySheet For(ScheduleDay day, RoundKind kind)
    {
        var (stops, round) = kind == RoundKind.Delivery
            ? (day.Deliveries, day.DeliveryRound)
            : (day.Collections, day.CollectionRound);
        return new DeliverySheet(day.Day, kind, round, stops, Load(stops), day.BusinessPoint);
    }

    /// <summary>Elk artikel van alle stops opgeteld; een pakket telt met de artikelen erin, niet als zichzelf.</summary>
    private static List<LoadingItem> Load(IEnumerable<ScheduleStop> stops) =>
        stops.SelectMany(s => s.Lines)
            .SelectMany(l => l.Contents.Count > 0 ? l.Contents : [l])
            .GroupBy(l => l.Name)
            .Select(g => new LoadingItem(g.Key, g.Sum(l => l.Quantity)))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

/// <summary>Eén regel van de laadlijst: een artikel en hoeveel er van mee moet.</summary>
public record LoadingItem(string Name, int Quantity);
