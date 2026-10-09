using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>
/// Links waarmee een gebruiker zelf een wachtwoord kiest: in een uitnodiging en bij wachtwoord vergeten.
/// Ze dragen een Identity-token van 48 uur dat ongeldig wordt zodra het wachtwoord gezet is.
/// </summary>
internal static class PasswordLinks
{
    public static async Task<string> CreateAsync(UserManager<User> userManager, User user, Uri baseUri, string path)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return new Uri(baseUri, $"{path}?gebruiker={user.Id}&code={code}").ToString();
    }

    /// <summary>Zet het wachtwoord met de code uit de link. Geeft een foutboodschap terug, of null als het gelukt is.</summary>
    public static async Task<string?> SetPasswordAsync(UserManager<User> userManager, User user, string? code, string password, string invalidLink)
    {
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code ?? "")); }
        catch (FormatException) { return invalidLink; }

        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? invalidLink
                : string.Join(" ", result.Errors.Select(e => e.Description));

        // Wie via de mail een wachtwoord kiest, heeft zijn e-mailadres bevestigd en is niet langer geblokkeerd.
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        return null;
    }
}
