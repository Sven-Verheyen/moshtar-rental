using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Tenancy;

public class TenancyOptions
{
    /// <summary>
    /// Tenant die gebruikt wordt als geen enkele domeinnaam overeenkomt.
    /// Handig in ontwikkeling (localhost); in productie leeg laten.
    /// </summary>
    public string? FallbackTenantSlug { get; set; }
}

/// <summary>Zoekt tenants op domeinnaam, met een korte cache zodat niet elk request de database raakt.</summary>
public interface ITenantStore
{
    Tenant? FindByHost(string host);

    /// <summary>Vergeet de bewaarde gegevens van deze verhuurder, zodat het volgende request ze opnieuw leest.</summary>
    void Forget(Guid tenantId);
}

internal sealed class TenantStore(IServiceScopeFactory scopeFactory, IMemoryCache cache, IOptions<TenancyOptions> options) : ITenantStore
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _expirations = new();
    private long _generation;

    public Tenant? FindByHost(string host)
    {
        host = host.ToLowerInvariant();
        var key = $"tenant-host:{host}";
        if (cache.TryGetValue(key, out Tenant? cached)) return cached;

        // Wat gelezen werd vóór een Forget mag niet meer in de cache: misschien is het al verouderd.
        var generation = Interlocked.Read(ref _generation);
        var tenant = Load(host);
        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration };
        if (tenant is not null)
            options.AddExpirationToken(new CancellationChangeToken(_expirations.GetOrAdd(tenant.Id, _ => new CancellationTokenSource()).Token));
        if (Interlocked.Read(ref _generation) == generation)
            cache.Set(key, tenant, options);
        return tenant;
    }

    public void Forget(Guid tenantId)
    {
        Interlocked.Increment(ref _generation);
        // Niet disposen: een lopende FindByHost kan het token nog net opvragen; het is dan al geannuleerd.
        if (_expirations.TryRemove(tenantId, out var expiration))
            expiration.Cancel();
    }

    private Tenant? Load(string host)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = db.Tenants.AsNoTracking().Include(t => t.Hosts)
            .FirstOrDefault(t => t.Hosts.Any(h => h.Hostname == host));
        if (tenant is null && options.Value.FallbackTenantSlug is { Length: > 0 } slug)
            tenant = db.Tenants.AsNoTracking().FirstOrDefault(t => t.Slug == slug);
        return tenant;
    }
}
