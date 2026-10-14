using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Reservations;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De blokkeringen beheren. Na het bewaren geeft <see cref="ShortageQuery"/> de tekorten van het artikel die het bewaren
/// veroorzaakte, net als bij een lagere voorraad in <see cref="RentalItemAdministration"/>.
/// </summary>
internal sealed class BlockoutAdministration(AppDbContext db, ITenantContext tenantContext, TimeProvider clock, ShortageQuery shortages)
    : IBlockoutAdministration
{
    private const int MaxReasonLength = 200;

    public async Task<IReadOnlyList<BlockoutSummary>> GetCurrentAsync(Guid? rentalItemId = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var blockouts = await db.Blockouts.AsNoTracking()
            .Where(b => b.EndDate == null || b.EndDate >= today)
            .Where(b => rentalItemId == null || b.RentalItemId == rentalItemId)
            .ToListAsync(ct);
        var itemIds = blockouts.Select(b => b.RentalItemId).Distinct().ToList();
        var items = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return blockouts
            .Select(b => new BlockoutSummary(b.Id, Name(items[b.RentalItemId]),
                new BlockoutInput(b.RentalItemId, b.Quantity, b.StartDate, b.EndDate, b.Reason)))
            .OrderBy(b => b.Input.StartDate).ThenBy(b => b.RentalItemName).ThenBy(b => b.Id)
            .ToList();
    }

    public async Task<BlockoutSaved> CreateAsync(BlockoutInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var blockout = new Blockout();
        await ApplyAsync(blockout, input, ct);
        var before = await shortages.FindAsync(blockout.RentalItemId, ct);
        db.Blockouts.Add(blockout);
        await db.SaveChangesAsync(ct);
        return await SavedAsync(blockout, before, ct);
    }

    public async Task<BlockoutSaved> SaveAsync(Guid blockoutId, BlockoutInput input, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var blockout = await FindAsync(blockoutId, ct);
        await ApplyAsync(blockout, input, ct);
        var before = await shortages.FindAsync(blockout.RentalItemId, ct);
        await db.SaveChangesAsync(ct);
        return await SavedAsync(blockout, before, ct);
    }

    /// <summary>De bewaarde blokkering met de tekorten die het bewaren veroorzaakte of erger maakte (niet die er al waren).</summary>
    private async Task<BlockoutSaved> SavedAsync(Blockout blockout, IReadOnlyList<Shortage> before, CancellationToken ct) =>
        new(blockout.Id, ShortageCalculator.NewOrWorse(before, await shortages.FindAsync(blockout.RentalItemId, ct)));

    public async Task DeleteAsync(Guid blockoutId, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        db.Blockouts.Remove(await FindAsync(blockoutId, ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Controleert wat de beheerder ingaf en zet het op de blokkering; gooit een <see cref="BlockoutException"/> als het niet klopt.</summary>
    private async Task ApplyAsync(Blockout blockout, BlockoutInput input, CancellationToken ct)
    {
        var reason = input.Reason?.Trim() is { Length: > 0 } r ? r : null;
        var problems = new List<string>();
        if (input.Quantity < 1) problems.Add("Blokkeer minstens 1 exemplaar.");
        if (input.EndDate < input.StartDate) problems.Add("De einddatum ligt voor de begindatum.");
        if (reason?.Length > MaxReasonLength) problems.Add($"De reden is langer dan {MaxReasonLength} tekens.");
        if (problems.Count > 0) throw new BlockoutException(problems);

        var item = await db.RentalItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == input.RentalItemId, ct)
            ?? throw new BlockoutException(["Dit artikel bestaat niet (meer)."]);
        if (input.Quantity > item.Stock)
            throw new BlockoutException([item.Stock == 1
                ? $"Van \"{Name(item)}\" heb je maar 1 exemplaar."
                : $"Van \"{Name(item)}\" heb je maar {item.Stock} exemplaren."]);

        blockout.RentalItemId = input.RentalItemId;
        blockout.Quantity = input.Quantity;
        blockout.StartDate = input.StartDate;
        blockout.EndDate = input.EndDate;
        blockout.Reason = reason;
    }

    private async Task<Blockout> FindAsync(Guid blockoutId, CancellationToken ct) =>
        await db.Blockouts.FirstOrDefaultAsync(b => b.Id == blockoutId, ct)
            ?? throw new BlockoutException(["Deze blokkering bestaat niet (meer)."]);

    private string Name(RentalItem item) => item.Translations.For(Tenant.DefaultCulture, Tenant.DefaultCulture)?.Name ?? item.Slug;

    private Domain.Tenants.Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
