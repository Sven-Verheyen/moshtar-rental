using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Application.Catalog;
using Moshtar.Application.Mail;
using Moshtar.Infrastructure.Reservations;
using Moshtar.Infrastructure.Catalog;
using Moshtar.Infrastructure.Mail;
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
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<ICatalogService, CatalogService>();

        var mail = configuration.GetSection("Mail");
        services.Configure<MailOptions>(mail);
        services.AddScoped<IMailer, Mailer>();
        if (string.IsNullOrWhiteSpace(mail[nameof(MailOptions.AzureCommunicationServicesConnectionString)]))
        {
            services.AddSingleton<RecordingMailTransport>();
            services.AddSingleton<IMailTransport>(sp => sp.GetRequiredService<RecordingMailTransport>());
        }
        else
        {
            services.AddSingleton<IMailTransport, AzureMailTransport>();
        }
        return services;
    }
}
