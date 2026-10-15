using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Carts;
using Moshtar.Application.Catalog;
using Moshtar.Application.Prices;
using Moshtar.Domain.Carts;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Carts;

internal sealed class CartAdministration(AppDbContext db, ICatalogService catalog, IPriceQuotes prices) : ICartAdministration
{
    public async Task<IReadOnlyList<OpenCartView>> ListOpenAsync(CancellationToken ct = default)
    {
        var carts = await db.Carts.AsNoTracking()
            .Where(c => c.ReservedAtUtc == null && c.Lines.Count > 0)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToListAsync(ct);
        // Het back office is Nederlandstalig.
        var entries = (await catalog.GetCatalogAsync("nl", ct)).ToDictionary(e => e.Ref());
        // Met de prijsregels van vandaag, zoals de klant zijn winkelkar nu ziet.
        var quoter = await prices.CurrentAsync(ct);

        return carts.Select(c =>
        {
            var lines = c.Lines.Select(l => (Line: l, Entry: entries.GetValueOrDefault(l.Entry))).ToList();
            var priced = quoter.Quote(c.Period, lines.Where(x => x.Entry is not null).Select(x => x.Entry!.ToPriceLine(x.Line.Quantity)).ToList());
            return new OpenCartView(
                c.Email,
                c.Period,
                lines.Select(x => new OpenCartLine(x.Entry?.Name, x.Line.Quantity)).ToList(),
                priced.Total,
                c.UpdatedAtUtc,
                c.ReminderSentAtUtc,
                c.ReminderKey is not null && c.ReminderSentAtUtc is null);
        }).ToList();
    }
}
