using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Reviews;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Site;

/// <summary>
/// Mailt elk kwartier de reviewvragen die moeten vertrekken, voor elke verhuurder met "Reviewvragen sturen" aan apart
/// (zie <see cref="IReviewRequests"/>). Een fout bij één verhuurder houdt de andere niet tegen.
/// </summary>
internal sealed class ReviewRequestWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReviewRequestWorker> logger) : BackgroundService
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
            tenants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking()
                .Where(t => t.ReviewRequestsSince != null).Include(t => t.Hosts).ToListAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Reviewvragen: de verhuurders konden niet gelezen worden.");
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
                var sent = await scope.ServiceProvider.GetRequiredService<IReviewRequests>().SendDueAsync(clock.GetUtcNow().UtcDateTime, ct);
                if (sent > 0)
                    logger.LogInformation("Reviewvragen van {Tenant}: {Sent} mail(s) verstuurd.", tenant.Slug, sent);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Reviewvragen van verhuurder {Tenant} versturen mislukt.", tenant.Slug);
            }
        }
    }
}
