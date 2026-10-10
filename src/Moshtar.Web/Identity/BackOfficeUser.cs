using System.Security.Claims;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Identity;

public static class BackOfficeUser
{
    /// <summary>De ingelogde gebruiker als actor in de historiek van een reservatie.</summary>
    public static ReservationActor AsReservationActor(this ClaimsPrincipal user) =>
        ReservationActor.User(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Geen ingelogde gebruiker.")));
}
