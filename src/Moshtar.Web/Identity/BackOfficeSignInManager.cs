using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>Uitgeschakelde gebruikers kunnen niet inloggen.</summary>
internal sealed class BackOfficeSignInManager(
    UserManager<User> userManager, IHttpContextAccessor contextAccessor, IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor, ILogger<SignInManager<User>> logger, IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(User user) => user.IsActive && await base.CanSignInAsync(user);
}
