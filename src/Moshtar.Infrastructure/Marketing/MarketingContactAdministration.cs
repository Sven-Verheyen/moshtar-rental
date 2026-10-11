using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Marketing;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Marketing;

internal sealed class MarketingContactAdministration(AppDbContext db) : IMarketingContactAdministration
{
    public async Task<IReadOnlyList<MarketingContactView>> ListAsync(CancellationToken ct = default) =>
        await db.MarketingContacts.AsNoTracking()
            .OrderByDescending(c => c.ConsentedAtUtc).ThenBy(c => c.Email)
            .Select(c => new MarketingContactView(c.Email, c.Culture, c.ConsentedAtUtc, c.Source))
            .ToListAsync(ct);
}
