using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;

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

    /// <summary>
    /// Het punt van het bedrijfsadres, waar elke ronde vertrekt en eindigt. Ontbreekt het (nog nooit opgezocht, of het
    /// adres veranderde), dan wordt het opgezocht en op <paramref name="tenant"/> gezet; bewaren doet de oproeper.
    /// Null zonder bedrijfsadres, of als het niet gevonden wordt.
    /// </summary>
    public static async Task<GeoPoint?> FindBusinessPointAsync(this IMapService maps, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        if (tenant.AddressPoint is null && !string.IsNullOrWhiteSpace(tenant.Street) && !string.IsNullOrWhiteSpace(tenant.PostalCode))
            tenant.AddressPoint = await maps.TryFindAsync(new Address { Street = tenant.Street, PostalCode = tenant.PostalCode, City = tenant.City ?? "" }, logger, ct);
        return tenant.AddressPoint;
    }
}
