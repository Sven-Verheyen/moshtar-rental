using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Carts;
using Moshtar.Application.Catalog;
using Moshtar.Domain.Carts;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Carts;

internal sealed class CartAdministration(AppDbContext db, ICatalogService catalog) : ICartAdministration
{
    public async Task<IReadOnlyList<OpenCartView>> ListOpenAsync(CancellationToken ct = default)
    {
        var carts = await db.Carts.AsNoTracking()
            .Where(c => c.ReservedAtUtc == null && c.Lines.Count > 0)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToListAsync(ct);
        // Het back office is Nederlandstalig.
        var entries = (await catalog.GetCatalogAsync("nl", ct)).ToDictionary(e =>
            e.Kind == CatalogEntryKind.Item ? CatalogRef.Item(e.Id) : CatalogRef.Bundle(e.Id));

        return carts.Select(c =>
        {
            var days = c.Period.Days;
            var lines = c.Lines.Select(l => (Line: l, Entry: entries.GetValueOrDefault(l.Entry))).ToList();
            return new OpenCartView(
                c.Email,
                c.Period,
                lines.Select(x => new OpenCartLine(x.Entry?.Name, x.Line.Quantity)).ToList(),
                lines.Sum(x => (x.Entry?.PriceFor(days) ?? 0) * x.Line.Quantity),
                c.UpdatedAtUtc,
                c.ReminderSentAtUtc);
        }).ToList();
    }
}
