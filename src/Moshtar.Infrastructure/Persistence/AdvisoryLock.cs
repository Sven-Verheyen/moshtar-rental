using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Moshtar.Infrastructure.Persistence;

/// <summary>
/// Een PostgreSQL advisory lock op een sleutel, tot het einde van de lopende transactie: wie dezelfde sleutel vraagt,
/// wacht tot de vorige houder klaar is.
/// </summary>
internal static class AdvisoryLock
{
    public static Task AcquireAdvisoryLockAsync(this DatabaseFacade database, string key, CancellationToken ct) =>
        database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);

    /// <summary>
    /// Probeert een advisory lock op deze sleutel voor de verbinding, los van transacties: null als iemand anders hem
    /// heeft. Tot het vrijgeven blijft de verbinding open; sterft het proces, dan valt de lock weg met de verbinding.
    /// </summary>
    public static async Task<IAsyncDisposable?> TryAcquireSessionAdvisoryLockAsync(this DatabaseFacade database, string key, CancellationToken ct)
    {
        await database.OpenConnectionAsync(ct);
        var acquired = false;
        try
        {
            acquired = await database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({key}, 0)) AS \"Value\"").SingleAsync(ct);
            return acquired ? new SessionLock(database, key) : null;
        }
        finally
        {
            if (!acquired) await database.CloseConnectionAsync();
        }
    }

    private sealed class SessionLock(DatabaseFacade database, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({key}, 0)) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
        }
    }
}
