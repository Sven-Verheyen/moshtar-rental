using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Identity;

/// <summary>Gebruikers aanmaken buiten een request om: bij de installatie en in de demo-data.</summary>
public static class BackOfficeUsers
{
    /// <summary>
    /// Maakt een gebruiker aan bij de verhuurder met deze slug. Bestaat het e-mailadres daar al,
    /// dan gebeurt er niets.
    /// </summary>
    public static async Task<User> EnsureAsync(IServiceProvider services, string tenantSlug, string email, string password, UserRole role)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug)
            ?? throw new InvalidOperationException($"Verhuurder '{tenantSlug}' bestaat niet.");
        services.GetRequiredService<HostTenantContext>().UseTenant(tenant);

        var userManager = services.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByEmailAsync(email) is { } existing) return existing;

        var user = new User { TenantId = tenant.Id, UserName = email, Email = email, EmailConfirmed = true, Role = role };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        return user;
    }
}
