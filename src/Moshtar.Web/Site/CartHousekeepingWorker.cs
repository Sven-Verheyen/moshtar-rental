using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Carts;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Site;

/// <summary>
/// Doet elk kwartier het onderhoud van de winkelkarren, voor elke verhuurder apart (herinneringen en opruimen, zie
/// <see cref="ICartHousekeeping"/>). Een fout bij één verhuurder houdt de andere niet tegen.
/// </summary>
internal sealed class CartHousekeepingWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<CartHousekeepingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Domain.Tenants.Tenant> tenants;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            tenants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking().Include(t => t.Hosts).ToListAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Onderhoud van de winkelkarren: de verhuurders konden niet gelezen worden.");
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
                var result = await scope.ServiceProvider.GetRequiredService<ICartHousekeeping>().RunAsync(clock.GetUtcNow().UtcDateTime, ct);
                if (result.RemindersSent > 0 || result.CartsDeleted > 0)
                    logger.LogInformation("Winkelkarren van {Tenant}: {Sent} herinnering(en) verstuurd, {Deleted} gewist.",
                        tenant.Slug, result.RemindersSent, result.CartsDeleted);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Onderhoud van de winkelkarren van verhuurder {Tenant} mislukt.", tenant.Slug);
            }
        }
    }
}
