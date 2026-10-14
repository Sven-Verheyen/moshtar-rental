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
using Moshtar.Application.Images;
using Moshtar.Infrastructure.Images;

namespace Moshtar.Infrastructure;

public static class DependencyInjection
{
    /// <param name="sendRealMail">
    /// Mag er echt gemaild worden? Lokaal en in tests niet, ook niet als er een connection string ingesteld is.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool sendRealMail)
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
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICategoryAdministration, CategoryAdministration>();
        services.AddScoped<OldSlugs>();
        services.AddScoped<IOldSlugs>(sp => sp.GetRequiredService<OldSlugs>());
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

        // Afbeeldingen (ADR 0005): Azure Blob Storage, of zonder connection string een lokale map, zoals bij mail.
        var images = configuration.GetSection("Images");
        services.Configure<ImageOptions>(images);
        if (string.IsNullOrWhiteSpace(images[nameof(ImageOptions.AzureBlobStorageConnectionString)]))
        {
            var path = images[nameof(ImageOptions.LocalPath)] is { Length: > 0 } configured
                ? configured
                : Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "afbeeldingen");
            Directory.CreateDirectory(path);
            services.AddSingleton(new LocalImageFolder(Path.GetFullPath(path)));
            services.AddScoped<IImageStore, LocalImageStore>();
        }
        else
        {
            services.AddSingleton<ImageContainer>();
            services.AddScoped<IImageStore, AzureBlobImageStore>();
        }
        return services;
    }
}
