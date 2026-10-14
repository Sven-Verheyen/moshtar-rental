using Moshtar.Domain.Common;

namespace Moshtar.Application.Maps;

/// <summary>
/// De kaartdienst (ADR 0006): adressen opzoeken, en later ook routes berekenen. In productie zit Azure Maps erachter;
/// zonder sleutel (lokaal, tests) een voorspelbare vervanger.
/// </summary>
public interface IMapService
{
    /// <summary>
    /// Het punt van dit adres, of null als het niet (precies genoeg) gevonden wordt: een adres dat enkel tot op de
    /// straat of de gemeente gevonden wordt, telt als niet gevonden. Gooit een uitzondering als de dienst niet antwoordt.
    /// </summary>
    Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default);
}
