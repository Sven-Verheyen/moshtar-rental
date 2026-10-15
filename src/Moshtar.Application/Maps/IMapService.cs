using Moshtar.Domain.Common;

namespace Moshtar.Application.Maps;

/// <summary>
/// De kaartdienst (ADR 0006): adressen opzoeken en routes berekenen. In productie zit Azure Maps erachter;
/// zonder sleutel (lokaal, tests) een voorspelbare vervanger.
/// </summary>
public interface IMapService
{
    /// <summary>
    /// Het punt van dit adres, of null als het niet (precies genoeg) gevonden wordt: een adres dat enkel tot op de
    /// straat of de gemeente gevonden wordt, telt als niet gevonden. Gooit een uitzondering als de dienst niet antwoordt.
    /// </summary>
    Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default);

    /// <summary>
    /// De route vanaf <paramref name="start"/> langs alle <paramref name="stops"/> en terug naar <paramref name="start"/>,
    /// met het verwachte verkeer bij vertrek op <paramref name="departure"/>. Met <paramref name="bestOrder"/> kiest de
    /// dienst de snelste volgorde, anders rijdt de route de stops in de gegeven volgorde. Gooit een uitzondering als de
    /// dienst niet antwoordt.
    /// </summary>
    Task<Route> RouteAsync(GeoPoint start, IReadOnlyList<GeoPoint> stops, DateTimeOffset departure, bool bestOrder, CancellationToken ct = default);

    /// <summary>
    /// De reistijd van elk punt van <paramref name="origins"/> naar elk punt van <paramref name="destinations"/>
    /// ([herkomst][bestemming]), met het verwachte verkeer op <paramref name="departure"/>. Om een nieuwe stop in een
    /// bestaande ronde in te voegen.
    /// </summary>
    Task<TimeSpan[][]> TravelTimesAsync(IReadOnlyList<GeoPoint> origins, IReadOnlyList<GeoPoint> destinations, DateTimeOffset departure, CancellationToken ct = default);
}

/// <summary>
/// Een berekende route. <see cref="Order"/> zegt welke stop (als index in de gevraagde stops) eerst komt, dan welke
/// daarna, enz. <see cref="Legs"/> zijn de reistijden van elk stuk in die volgorde: naar de eerste stop, naar de
/// volgende, en als laatste terug naar het vertrekpunt (dus één meer dan er stops zijn).
/// </summary>
public sealed record Route(IReadOnlyList<int> Order, IReadOnlyList<TimeSpan> Legs);
