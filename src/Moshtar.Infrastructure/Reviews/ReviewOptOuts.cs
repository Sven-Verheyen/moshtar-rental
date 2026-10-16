using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Reviews;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reviews;

internal sealed class ReviewOptOuts(AppDbContext db, TimeProvider clock) : IReviewOptOuts
{
    public async Task OptOutAsync(string email, CancellationToken ct = default)
    {
        var normalized = ReviewOptOut.Normalize(email);
        if (normalized.Length == 0 || await IsOptedOutAsync(normalized, ct)) return;
        var optOut = new ReviewOptOut { Email = normalized, OptedOutAtUtc = clock.GetUtcNow().UtcDateTime };
        db.Add(optOut);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Twee keer tegelijk afgemeld: het eerste telt.
            db.Entry(optOut).State = EntityState.Detached;
            if (!await IsOptedOutAsync(normalized, ct)) throw;
        }
    }

    public Task<bool> IsOptedOutAsync(string email, CancellationToken ct = default)
    {
        var normalized = ReviewOptOut.Normalize(email);
        return db.ReviewOptOuts.AnyAsync(o => o.Email == normalized, ct);
    }

    public async Task<IReadOnlySet<string>> OptedOutAmongAsync(IEnumerable<string> emails, CancellationToken ct = default)
    {
        var normalized = emails.Select(ReviewOptOut.Normalize).Distinct().ToList();
        if (normalized.Count == 0) return new HashSet<string>();
        return (await db.ReviewOptOuts.Where(o => normalized.Contains(o.Email)).Select(o => o.Email).ToListAsync(ct)).ToHashSet();
    }
}
