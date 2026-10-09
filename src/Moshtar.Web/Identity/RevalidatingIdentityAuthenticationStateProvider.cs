using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>
/// Controleert in een open back-officescherm elke 30 minuten of de login nog geldig is,
/// zodat een gewijzigd wachtwoord of een uitgeschakelde gebruiker ook daar doorwerkt.
/// </summary>
internal sealed class RevalidatingIdentityAuthenticationStateProvider(
    ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory, IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.GetUserAsync(state.User);
        if (user is null) return false;
        if (!userManager.SupportsUserSecurityStamp) return true;
        var stamp = state.User.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        return stamp == await userManager.GetSecurityStampAsync(user);
    }
}
