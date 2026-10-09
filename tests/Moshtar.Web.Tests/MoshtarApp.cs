using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Identity;
using Moshtar.Web.Tenancy;
using Npgsql;

namespace Moshtar.Web.Tests;

/// <summary>
/// De webapp tegen een echte PostgreSQL, met een eigen database per testklasse.
/// Server: MOSHTAR_TEST_DB, standaard de lokale ontwikkeldatabase-server.
/// Twee verhuurders: Hopsakee.fun op hopsakee.test en Andere op andere.test.
/// </summary>
public sealed class MoshtarApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string HopsakeeHost = "hopsakee.test";
    public const string AndereHost = "andere.test";
    public const string Password = "een lang wachtwoord";

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("MOSHTAR_TEST_DB") ?? "Host=localhost;Port=5432;Username=moshtar;Password=moshtar")
    {
        Database = $"moshtar_test_{Guid.NewGuid():N}",
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Moshtar", _connectionString);
        builder.UseSetting("Tenancy:FallbackTenantSlug", "");
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.Tenants.AddRange(NewTenant("Hopsakee.fun", "hopsakee", HopsakeeHost), NewTenant("Andere", "andere", AndereHost));
        await db.SaveChangesAsync();
    }

    /// <summary>Maakt een gebruiker aan bij de verhuurder met deze slug.</summary>
    public async Task<User> CreateUserAsync(string tenantSlug, string email, UserRole role = UserRole.Administrator, string password = Password)
    {
        await using var scope = Services.CreateAsyncScope();
        return await BackOfficeUsers.EnsureAsync(scope.ServiceProvider, tenantSlug, email, password, role);
    }

    /// <summary>Een scope die werkt namens de verhuurder met deze slug, zoals een request op zijn domein.</summary>
    public async Task<AsyncServiceScope> TenantScopeAsync(string tenantSlug)
    {
        var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == tenantSlug);
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
        return scope;
    }

    /// <summary>Een browser die ingelogd is als een nieuwe gebruiker van Hopsakee.fun met deze rol.</summary>
    public async Task<HttpClient> LoggedInClientAsync(string email, UserRole role)
    {
        await CreateUserAsync("hopsakee", email, role);
        var client = CreateClient(HopsakeeHost);
        var login = await BackOffice.LoginAsync(client, email, Password);
        if (login.StatusCode != System.Net.HttpStatusCode.Redirect)
            throw new InvalidOperationException($"Inloggen als {email} mislukt.");
        return client;
    }

    public HttpClient CreateClient(string host) =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{host}"), AllowAutoRedirect = false });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
    }

    private static Tenant NewTenant(string name, string slug, string host)
    {
        var tenant = new Tenant { Name = name, Slug = slug, DefaultCulture = "nl", SupportedCultures = "nl,fr,en" };
        tenant.Hosts = [new TenantHost { Hostname = host, TenantId = tenant.Id }];
        return tenant;
    }
}
