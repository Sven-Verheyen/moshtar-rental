using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;

namespace Moshtar.Infrastructure.Maps;

internal static class MapServiceExtensions
{
    /// <summary>
    /// Zoekt het punt van een adres, maar een kaartdienst die niet antwoordt doet niets mislukken: dan blijft het
    /// punt leeg, zoals bij een adres dat niet gevonden wordt, en toont de planning een waarschuwing.
    /// </summary>
    public static async Task<GeoPoint?> TryFindAsync(this IMapService maps, Address address, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await maps.FindAsync(address, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Enkel de postcode: de straat van een klant hoort niet in de logs.
            logger.LogWarning(e, "Een adres in postcode {PostalCode} kon niet opgezocht worden.", address.PostalCode);
            return null;
        }
    }
}
