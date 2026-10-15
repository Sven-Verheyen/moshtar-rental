using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Maps;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Persistence;

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
    /// adres veranderde), dan wordt het opgezocht en bewaard, maar enkel als het adres intussen niet veranderde en
    /// niemand anders al een punt bewaarde: anders kwam het punt van het oude adres bij het nieuwe. Geeft het punt
    /// zoals het daarna bewaard is; null zonder bedrijfsadres, of als het niet gevonden wordt.
    /// </summary>
    public static async Task<GeoPoint?> FindBusinessPointAsync(this IMapService maps, AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.Street, t.PostalCode, t.City, t.AddressPoint })
            .SingleOrDefaultAsync(ct);
        if (tenant is null || tenant.AddressPoint is not null || string.IsNullOrWhiteSpace(tenant.Street) || string.IsNullOrWhiteSpace(tenant.PostalCode))
            return tenant?.AddressPoint;
        if (await maps.TryFindAsync(new Address { Street = tenant.Street, PostalCode = tenant.PostalCode, City = tenant.City ?? "" }, logger, ct) is not { } point)
            return null;

        await db.Tenants
            .Where(t => t.Id == tenantId && t.Street == tenant.Street && t.PostalCode == tenant.PostalCode && t.City == tenant.City && t.AddressPoint == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AddressPoint!.Latitude, point.Latitude)
                .SetProperty(t => t.AddressPoint!.Longitude, point.Longitude), ct);
        return await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.AddressPoint).SingleAsync(ct);
    }
}
