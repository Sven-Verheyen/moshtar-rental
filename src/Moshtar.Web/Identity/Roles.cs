using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Identity;

/// <summary>Rolnamen voor <c>[Authorize(Roles = ...)]</c> en <c>AuthorizeView</c>.</summary>
public static class Roles
{
    /// <summary>Enkel Beheerders: artikelen, pakketten, prijzen, voorraad, instellingen en gebruikers.</summary>
    public const string Administrator = nameof(UserRole.Administrator);
}
