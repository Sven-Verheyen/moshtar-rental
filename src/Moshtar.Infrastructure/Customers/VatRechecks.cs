using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Customers;
using Moshtar.Domain.Customers;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Customers;

internal sealed class VatRechecks(AppDbContext db, IVatRegistry vatRegistry) : IVatRechecks
{
    /// <summary>
    /// Vraagt de btw-databank naar de EU-btw-nummers die nog niet gecontroleerd zijn, de langst wachtende eerst en
    /// hoogstens <see cref="IVatRechecks.BatchSize"/>; geeft terug hoeveel ze gevraagd werd.
    /// </summary>
    public async Task<int> RecheckAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var before = nowUtc - IVatRechecks.RetryAfter;
        var pending = await db.Customers
            .Where(c => c.VatCheck == VatCheckStatus.NotChecked && c.VatNumber != null && (c.VatCheckedAtUtc == null || c.VatCheckedAtUtc <= before))
            .OrderBy(c => c.VatCheckedAtUtc.HasValue).ThenBy(c => c.VatCheckedAtUtc).ThenBy(c => c.Id)
            .Take(IVatRechecks.BatchSize)
            .ToListAsync(ct);
        var asked = 0;
        foreach (var customer in pending)
        {
            if (!customer.AwaitsVatCheck)
            {
                // Bv. een nummer van buiten de EU dat zonder controle binnenkwam: dat vraagt de databank nooit.
                customer.VatCheck = VatCheckStatus.NotCheckable;
                continue;
            }
            customer.RecordVatCheck(await vatRegistry.LookUpAsync(customer.VatNumber!, ct), nowUtc);
            asked++;
        }
        await db.SaveChangesAsync(ct);
        return asked;
    }
}
