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
}
