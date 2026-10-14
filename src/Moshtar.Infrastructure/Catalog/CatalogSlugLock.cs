using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// Artikelen en pakketten delen hun padwoord ("/huren/…"), dus hun slugs: wie er een wijzigt, doet dat als enige bij de
/// verhuurder, binnen een transactie met een PostgreSQL advisory lock. Zo geven twee beheerders niet tegelijk dezelfde
/// slug aan een artikel en een pakket (een unieke index kan dat over twee tabellen niet afdwingen).
/// </summary>
internal static class CatalogSlugLock
{
    public static async Task<IDbContextTransaction> AcquireAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = $"artikel-slugs-{tenantId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        return transaction;
    }
}
