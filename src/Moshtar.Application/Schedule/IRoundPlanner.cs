using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Schedule;

/// <summary>
/// De route van een ronde maken en bijsturen, voor Beheerder en Medewerker. Elke wijziging rekent de aankomsturen en
/// verwachte tijdsvensters opnieuw uit en bewaart de ronde. Stops zonder punt op de kaart gaan niet mee in de route.
/// Gooit een <see cref="InvalidOperationException"/> met een uitleg voor de gebruiker als het niet kan (geen
/// bedrijfsadres op de kaart, nog geen route, de kaartdienst antwoordt niet).
/// </summary>
public interface IRoundPlanner
{
    /// <summary>
    /// Laat de kaartdienst de snelste volgorde kiezen, vanaf en terug naar het bedrijfsadres, met het verkeer op het
    /// vertrekuur, en bewaart de ronde. Een bestaande ronde houdt haar vertrekuur, vaste uren en eigen opbouw- of
    /// afbouwtijden.
    /// </summary>
    Task ProposeAsync(DateOnly day, RoundKind kind, CancellationToken ct = default);

    /// <summary>Zet een stop op een andere plaats in de ronde (vanaf 1); de andere stops schuiven op.</summary>
    Task MoveAsync(DateOnly day, RoundKind kind, Guid reservationId, int position, CancellationToken ct = default);

    /// <summary>Zet een stop op een vast uur, of haalt het vaste uur weg met null.</summary>
    Task SetFixedTimeAsync(DateOnly day, RoundKind kind, Guid reservationId, TimeOnly? time, CancellationToken ct = default);

    /// <summary>Een eigen opbouw- of afbouwtijd voor één stop, in minuten; null: de standaard uit de instellingen.</summary>
    Task SetHandlingMinutesAsync(DateOnly day, RoundKind kind, Guid reservationId, int? minutes, CancellationToken ct = default);

    /// <summary>Het vertrekuur van de ronde van deze dag.</summary>
    Task SetDepartureAsync(DateOnly day, RoundKind kind, TimeOnly departure, CancellationToken ct = default);

    /// <summary>
    /// Na "Ronde gewijzigd": weggevallen stops verdwijnen, en elke nieuwe stop komt op de plek met de kleinste extra
    /// reistijd, zonder de volgorde van de andere stops te wijzigen.
    /// </summary>
    Task UpdateAsync(DateOnly day, RoundKind kind, CancellationToken ct = default);
}
