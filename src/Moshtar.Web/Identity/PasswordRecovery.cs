using System.Net;
using Microsoft.AspNetCore.Identity;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>Wachtwoord vergeten: een herstelmail aanvragen en met de link een nieuw wachtwoord kiezen.</summary>
public sealed class PasswordRecovery(UserManager<User> userManager, ITenantContext tenantContext, IMailer mailer)
{
    public const string ResetPath = "admin/wachtwoord-herstellen";
    private const string InvalidLink = "Deze link is verlopen of al gebruikt. Vraag op de loginpagina een nieuwe aan.";

    /// <summary>
    /// Stuurt een herstelmail als het e-mailadres bij deze verhuurder hoort en de gebruiker actief is.
    /// Laat bewust niet weten of dat zo is, zodat niemand zo e-mailadressen kan aftasten.
    /// </summary>
    public async Task RequestAsync(string email, Uri baseUri)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive) return;

        var link = await PasswordLinks.CreateAsync(userManager, user, baseUri, ResetPath);
        var tenantName = tenantContext.Tenant!.Name;
        await mailer.SendAsync(new MailMessage(
            user.Email!,
            $"Nieuw wachtwoord voor het back office van {tenantName}",
            $"""
            <p>Hallo,</p>
            <p>Je vroeg een nieuw wachtwoord aan voor het back office van {WebUtility.HtmlEncode(tenantName)}.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Kies een nieuw wachtwoord</a></p>
            <p>Deze link is 48 uur geldig. Vroeg je niets aan? Dan mag je deze mail negeren; je wachtwoord blijft hetzelfde.</p>
            """,
            $"""
            Hallo,

            Je vroeg een nieuw wachtwoord aan voor het back office van {tenantName}.
            Kies een nieuw wachtwoord via deze link:

            {link}

            Deze link is 48 uur geldig. Vroeg je niets aan? Dan mag je deze mail negeren; je wachtwoord blijft hetzelfde.
            """));
    }

    /// <summary>Zet het nieuwe wachtwoord. Geeft een foutboodschap terug, of null als het gelukt is.</summary>
    public async Task<string?> ResetAsync(Guid userId, string? code, string password)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) return InvalidLink;
        return await PasswordLinks.SetPasswordAsync(userManager, user, code, password, InvalidLink);
    }
}
