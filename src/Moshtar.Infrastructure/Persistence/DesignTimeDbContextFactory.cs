using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Persistence;

/// <summary>Enkel voor <c>dotnet ef migrations</c>.</summary>
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=moshtar;Username=moshtar;Password=moshtar")
            .Options;
        return new AppDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Tenant? Tenant => null;
    }
}
