using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Customers;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Site;

/// <summary>
/// Controleert elk kwartier de btw-nummers die nog niet gecontroleerd zijn, voor elke verhuurder apart (zie
/// <see cref="IVatRechecks"/>: elke klant hoogstens één keer per uur, een beperkt aantal per run). Een fout bij één
/// verhuurder houdt de andere niet tegen.
/// </summary>
internal sealed class VatRecheckWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<VatRecheckWorker> logger) : BackgroundService
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
            logger.LogError(e, "Btw-controle: de verhuurders konden niet gelezen worden.");
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
                var asked = await scope.ServiceProvider.GetRequiredService<IVatRechecks>().RecheckAsync(clock.GetUtcNow().UtcDateTime, ct);
                if (asked > 0)
                    logger.LogInformation("Btw-controle van {Tenant}: {Asked} nummer(s) opnieuw gecontroleerd.", tenant.Slug, asked);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Btw-controle van verhuurder {Tenant} mislukt.", tenant.Slug);
            }
        }
    }
}
