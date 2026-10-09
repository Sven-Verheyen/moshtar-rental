using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>Een gebruiker zoals de Beheerder hem in de lijst ziet.</summary>
public sealed record UserSummary(Guid Id, string Email, UserRole Role, bool IsActive, bool HasAcceptedInvite);

/// <summary>Een regel van het gebruikersbeheer werd overtreden; de boodschap is voor de Beheerder bedoeld.</summary>
public sealed class UserAdministrationException(string message) : Exception(message);

/// <summary>
/// Gebruikersbeheer binnen de verhuurder van het huidige request: uitnodigen, rol wijzigen,
/// uitschakelen en weer inschakelen. Gebruikers worden nooit verwijderd, en er blijft altijd
/// minstens één actieve Beheerder.
/// </summary>
public sealed class UserAdministration(UserManager<User> userManager, ITenantContext tenantContext, IMailer mailer)
{
    public const string InvitePath = "admin/uitnodiging";

    public async Task<IReadOnlyList<UserSummary>> ListAsync()
    {
        var users = await userManager.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
        return users.Select(u => new UserSummary(u.Id, u.Email!, u.Role, u.IsActive, u.PasswordHash is not null)).ToList();
    }

    /// <summary>Maakt een gebruiker zonder wachtwoord aan en stuurt hem een link om er zelf een te kiezen.</summary>
    public async Task InviteAsync(string email, UserRole role, Uri baseUri)
    {
        email = email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            throw new UserAdministrationException($"Er bestaat al een gebruiker met e-mailadres {email}.");

        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
        var user = new User { TenantId = tenant.Id, UserName = email, Email = email, Role = role };
        Ensure(await userManager.CreateAsync(user));
        await SendInviteAsync(user, baseUri);
    }

    /// <summary>Stuurt een nieuwe link; de vorige link werkt dan niet meer.</summary>
    public async Task ResendInviteAsync(Guid userId, Uri baseUri)
    {
        var user = await FindAsync(userId);
        if (user.PasswordHash is not null)
            throw new UserAdministrationException($"{user.Email} heeft de uitnodiging al aanvaard.");
        Ensure(await userManager.UpdateSecurityStampAsync(user));
        await SendInviteAsync(user, baseUri);
    }

    public async Task ChangeRoleAsync(Guid userId, UserRole role)
    {
        var user = await FindAsync(userId);
        if (user.Role == role) return;
        if (role != UserRole.Administrator) await EnsureAnotherActiveAdministratorAsync(user);
        user.Role = role;
        // De rol zit in de login: een nieuwe security stamp laat ze meteen gelden.
        // UpdateSecurityStampAsync bewaart ook de rol, in één keer.
        Ensure(await userManager.UpdateSecurityStampAsync(user));
    }

    /// <summary>Schakelt een gebruiker uit en beëindigt zijn lopende sessies.</summary>
    public async Task DisableAsync(Guid userId)
    {
        var user = await FindAsync(userId);
        if (!user.IsActive) return;
        await EnsureAnotherActiveAdministratorAsync(user);
        user.IsActive = false;
        // Een nieuwe security stamp beëindigt lopende sessies; zelfde update als IsActive.
        Ensure(await userManager.UpdateSecurityStampAsync(user));
    }

    public async Task EnableAsync(Guid userId)
    {
        var user = await FindAsync(userId);
        if (user.IsActive) return;
        user.IsActive = true;
        Ensure(await userManager.UpdateAsync(user));
    }

    /// <summary>
    /// Zet het eerste wachtwoord via de link uit de uitnodiging.
    /// Geeft een foutboodschap terug, of null als het gelukt is.
    /// </summary>
    public async Task<string?> AcceptInviteAsync(Guid userId, string code, string password)
    {
        const string invalidLink = "Deze link is verlopen of al gebruikt. Vraag een Beheerder om een nieuwe uitnodiging.";
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.PasswordHash is not null) return invalidLink;

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return invalidLink; }

        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (result.Succeeded)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
            return null;
        }
        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
            ? invalidLink
            : string.Join(" ", result.Errors.Select(e => e.Description));
    }

    private async Task SendInviteAsync(User user, Uri baseUri)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = new Uri(baseUri, $"{InvitePath}?gebruiker={user.Id}&code={code}").ToString();
        var tenantName = tenantContext.Tenant!.Name;
        var role = user.Role == UserRole.Administrator ? "Beheerder" : "Medewerker";

        await mailer.SendAsync(new MailMessage(
            user.Email!,
            $"Uitnodiging voor het back office van {tenantName}",
            $"""
            <p>Hallo,</p>
            <p>Je bent uitgenodigd als {role} in het back office van {WebUtility.HtmlEncode(tenantName)}.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Kies je wachtwoord</a></p>
            <p>Deze link is 48 uur geldig.</p>
            """,
            $"""
            Hallo,

            Je bent uitgenodigd als {role} in het back office van {tenantName}.
            Kies je wachtwoord via deze link:

            {link}

            Deze link is 48 uur geldig.
            """));
    }

    private async Task<User> FindAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString())
        ?? throw new UserAdministrationException("Deze gebruiker bestaat niet.");

    private async Task EnsureAnotherActiveAdministratorAsync(User user)
    {
        if (user.Role != UserRole.Administrator || !user.IsActive) return;
        // Een uitnodiging die nog niet aanvaard is, telt niet: die persoon kan (nog) niet inloggen.
        var others = await userManager.Users.CountAsync(u =>
            u.Id != user.Id && u.IsActive && u.Role == UserRole.Administrator && u.PasswordHash != null);
        if (others == 0)
            throw new UserAdministrationException("Er moet minstens één actieve Beheerder overblijven.");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserAdministrationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
