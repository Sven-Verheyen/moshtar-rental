using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Reviews;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reviews;

/// <summary>Met een eigen, kortlevende context per oproep: zo kan ook een interactieve pagina (de Planning) ze gebruiken.</summary>
internal sealed class ReviewOptOuts(IDbContextFactory<AppDbContext> contexts, TimeProvider clock) : IReviewOptOuts
{
    public async Task OptOutAsync(string email, CancellationToken ct = default)
    {
        var normalized = ReviewOptOut.Normalize(email);
        if (normalized.Length == 0 || await IsOptedOutAsync(normalized, ct)) return;
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.Add(new ReviewOptOut { Email = normalized, OptedOutAtUtc = clock.GetUtcNow().UtcDateTime });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Twee keer tegelijk afgemeld: het eerste telt.
            if (!await IsOptedOutAsync(normalized, ct)) throw;
        }
    }

    public async Task<bool> IsOptedOutAsync(string email, CancellationToken ct = default)
    {
        var normalized = ReviewOptOut.Normalize(email);
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.ReviewOptOuts.AnyAsync(o => o.Email == normalized, ct);
    }

    public async Task<IReadOnlySet<string>> OptedOutAmongAsync(IEnumerable<string> emails, CancellationToken ct = default)
    {
        var normalized = emails.Select(ReviewOptOut.Normalize).Distinct().ToList();
        if (normalized.Count == 0) return new HashSet<string>();
        await using var db = await contexts.CreateDbContextAsync(ct);
        return (await db.ReviewOptOuts.AsNoTracking().Where(o => normalized.Contains(o.Email)).Select(o => o.Email).ToListAsync(ct)).ToHashSet();
    }
}
