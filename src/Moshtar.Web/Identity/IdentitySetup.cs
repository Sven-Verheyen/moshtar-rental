using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Identity;

internal static class IdentitySetup
{
    public const string LoginPath = "/admin/inloggen";
    public const string LogoutPath = "/admin/uitloggen";
    public const string AccessDeniedPath = "/admin/geen-toegang";
    public const string TenantClaim = "moshtar:tenant";

    /// <summary>Login voor het back office: e-mail en wachtwoord, per verhuurder.</summary>
    public static IServiceCollection AddBackOfficeIdentity(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider>();

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.AddAuthorization();

        services.AddIdentityCore<User>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;

                // Lange wachtwoorden in plaats van verplichte tekenklassen.
                o.Password.RequiredLength = 12;
                o.Password.RequiredUniqueChars = 1;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<UserClaimsFactory>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = ".Moshtar.BackOffice";
            o.LoginPath = LoginPath;
            o.LogoutPath = LogoutPath;
            o.AccessDeniedPath = AccessDeniedPath;
            // "Onthoud mij" geeft een cookie van 14 dagen; zonder vinkje is het een sessiecookie.
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.Events.OnValidatePrincipal = ValidatePrincipalAsync;
        });

        return services;
    }

    /// <summary>Een login geldt enkel voor de verhuurder waarbij hij gemaakt is.</summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (context.Principal?.FindFirstValue(TenantClaim) != tenant.TenantId.ToString())
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }
}

/// <summary>Zet de rol en de verhuurder van de gebruiker in zijn login.</summary>
internal sealed class UserClaimsFactory(UserManager<User> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(IdentitySetup.TenantClaim, user.TenantId.ToString()));
        return identity;
    }
}
