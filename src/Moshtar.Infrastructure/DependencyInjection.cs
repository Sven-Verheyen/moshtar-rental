using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Booking;
using Moshtar.Application.Catalog;
using Moshtar.Infrastructure.Booking;
using Moshtar.Infrastructure.Catalog;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Tenancy;

namespace Moshtar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Moshtar")
            ?? throw new InvalidOperationException("Connection string 'Moshtar' ontbreekt.");

        // Factory voor interactieve componenten (kortlevende contexts), en ook AppDbContext zelf als scoped service.
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString), ServiceLifetime.Scoped);
        services.AddMemoryCache();
        services.Configure<TenancyOptions>(configuration.GetSection("Tenancy"));
        services.AddSingleton<ITenantStore, TenantStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ICatalogService, CatalogService>();
        return services;
    }
}
