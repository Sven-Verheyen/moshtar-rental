using Microsoft.EntityFrameworkCore;

namespace Moshtar.Infrastructure.Persistence;

internal static class PrimaryHost
{
    /// <summary>Het hoofddomein van de verhuurder, voor de links in een mail; zonder hoofddomein een ander, zonder domein null.</summary>
    public static Task<string?> PrimaryHostAsync(this AppDbContext db, Guid tenantId, CancellationToken ct) =>
        db.TenantHosts.AsNoTracking().Where(h => h.TenantId == tenantId)
            .OrderByDescending(h => h.IsPrimary).Select(h => h.Hostname).FirstOrDefaultAsync(ct);
}
