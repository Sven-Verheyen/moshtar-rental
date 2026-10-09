using Microsoft.AspNetCore.Identity;

namespace Moshtar.Infrastructure.Identity;

/// <summary>
/// Een gebruiker van het back office. Hoort bij precies één verhuurder (zie ADR 0001):
/// hetzelfde e-mailadres kan bij een andere verhuurder een aparte gebruiker zijn.
/// </summary>
public class User : IdentityUser<Guid>
{
    public User() => Id = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public UserRole Role { get; set; }

    /// <summary>
    /// Uitgeschakelde gebruikers kunnen niet meer inloggen, maar blijven bestaan
    /// zodat zichtbaar blijft wie wat deed.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

public enum UserRole
{
    /// <summary>Beheerder: mag alles binnen zijn verhuurder.</summary>
    Administrator,
    /// <summary>Medewerker: volgt reservaties op.</summary>
    Staff,
}
