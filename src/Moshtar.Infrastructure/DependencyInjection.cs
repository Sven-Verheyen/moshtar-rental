using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Reservations;
using Moshtar.Application.Carts;
using Moshtar.Application.Catalog;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Reservations;
using Moshtar.Infrastructure.Carts;
using Moshtar.Infrastructure.Catalog;
using Moshtar.Infrastructure.Mail;
using Moshtar.Application.Marketing;
using Moshtar.Infrastructure.Marketing;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Infrastructure.Website;
using Moshtar.Application.Schedule;
using Moshtar.Infrastructure.Schedule;
using Moshtar.Application.Maps;
using Moshtar.Infrastructure.Maps;

namespace Moshtar.Infrastructure;

public static class DependencyInjection
{
    /// <param name="sendRealMail">
    /// Mag er echt gemaild worden? Lokaal en in tests niet, ook niet als er een connection string ingesteld is.
    /// </param>
    /// <param name="useAzureMaps">
    /// Mag Azure Maps gebruikt worden als er een sleutel ingesteld is? In de tests niet: daar is de kaartdienst altijd
    /// de lokale vervanger, zodat punten voorspelbaar zijn.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool sendRealMail, bool useAzureMaps)
    {
        var connectionString = configuration.GetConnectionString("Moshtar")
            ?? throw new InvalidOperationException("Connection string 'Moshtar' ontbreekt.");

        // Factory voor interactieve componenten (kortlevende contexts), en ook AppDbContext zelf als scoped service.
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString), ServiceLifetime.Scoped);
        services.AddMemoryCache();
        services.Configure<TenancyOptions>(configuration.GetSection("Tenancy"));
        services.AddSingleton<ITenantStore, TenantStore>();
        services.AddScoped<IBrandingAdministration, BrandingAdministration>();
        services.AddScoped<IBusinessDetailsAdministration, BusinessDetailsAdministration>();
        services.AddScoped<WebsiteTextAdministration>();
        services.AddScoped<IWebsiteTextAdministration>(sp => sp.GetRequiredService<WebsiteTextAdministration>());
        services.AddScoped<IWebsiteTexts>(sp => sp.GetRequiredService<WebsiteTextAdministration>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<ISchedule, ScheduleService>();
        services.AddScoped<IRoundPlanner, RoundPlanner>();
        services.AddScoped<IRoundMessages, RoundMessages>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICartHousekeeping, CartHousekeeping>();
        services.AddScoped<ICartAdministration, CartAdministration>();
        services.AddScoped<IMarketingContactAdministration, MarketingContactAdministration>();

        var mail = configuration.GetSection("Mail");
        services.Configure<MailOptions>(mail);
        services.AddScoped<IMailer, Mailer>();
        services.AddScoped<IMailTemplateAdministration, MailTemplateAdministration>();
        if (!sendRealMail || string.IsNullOrWhiteSpace(mail[nameof(MailOptions.AzureCommunicationServicesConnectionString)]))
        {
            services.AddSingleton(sp => new RecordingMailTransport(
                sp.GetRequiredService<ILogger<RecordingMailTransport>>(), logBody: !sendRealMail));
            services.AddSingleton<IMailTransport>(sp => sp.GetRequiredService<RecordingMailTransport>());
        }
        else
        {
            services.AddSingleton<IMailTransport, AzureMailTransport>();
        }

        var maps = configuration.GetSection("Maps");
        services.Configure<MapOptions>(maps);
        if (useAzureMaps && !string.IsNullOrWhiteSpace(maps[nameof(MapOptions.AzureMapsKey)]))
            // Kort geduld: een adres opzoeken gebeurt terwijl een klant op zijn reservatie wacht.
            services.AddHttpClient<IMapService, AzureMapService>(c =>
            {
                c.BaseAddress = new Uri(AzureMapService.BaseAddress);
                c.Timeout = TimeSpan.FromSeconds(5);
            });
        else
            services.AddSingleton<IMapService, LocalMapService>();
        return services;
    }
}
