using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De klanten van een ronde hun verwacht tijdsvenster laten weten, voor Beheerder en Medewerker. Elke stop onthoudt welk
/// venster de klant kreeg en wanneer.
/// </summary>
public interface IRoundMessages
{
    /// <summary>
    /// "Verwachte uren mailen": mailt elke klant van de ronde zijn verwacht tijdsvenster, in de taal van zijn reservatie,
    /// met het mailsjabloon Verwacht leveruur of Verwacht ophaaluur. Wie al gemaild werd, krijgt enkel opnieuw een mail
    /// als zijn venster sindsdien veranderde. Elke mail komt in de historiek van de reservatie. Geeft het aantal mails.
    /// Gooit een <see cref="InvalidOperationException"/> als de ronde nog geen route heeft.
    /// </summary>
    Task<int> MailExpectedTimesAsync(DateOnly day, RoundKind kind, ReservationActor actor, CancellationToken ct = default);
}
