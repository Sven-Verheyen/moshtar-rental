using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// Wijzigingen aan de artikelen en pakketten van een verhuurder gebeuren na elkaar, binnen een transactie met een
/// PostgreSQL advisory lock. Zo geven twee beheerders bijvoorbeeld niet tegelijk dezelfde slug aan een artikel en een
/// pakket (artikelen en pakketten delen hun padwoord "/huren/…", en een unieke index kan dat over twee tabellen niet
/// afdwingen).
/// </summary>
internal static class CatalogLock
{
    public static async Task<IDbContextTransaction> AcquireAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.AcquireAdvisoryLockAsync($"catalogus-{tenantId}", ct);
        return transaction;
    }
}
