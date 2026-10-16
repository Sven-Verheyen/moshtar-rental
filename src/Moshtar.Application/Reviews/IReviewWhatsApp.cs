using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Reviews;

/// <summary>
/// Het blok "Reviewvragen" in de dagweergave van de Planning, voor Beheerder en Medewerker. Staat "Reviewvragen sturen"
/// aan, dan staan er de klanten van wie de huurperiode 1 tot 3 dagen voor de getoonde dag eindigde, met dezelfde regels
/// als de mail: niet geannuleerd, einddag op of na het aanzetten, één per klant (e-mailadres) en einddag, zonder wie
/// zich afmeldde. Een klant op wiens WhatsApp-knop gedrukt werd, blijft die dag staan met het tijdstip en verdwijnt de
/// dag erna. Alleen voor vandaag of vroegere dagen, niet voor toekomstige.
/// </summary>
public interface IReviewWhatsApp
{
    /// <summary>De reviewvragen van deze dag, op einddag en dan op reservatienummer; leeg als de schakelaar uit staat.</summary>
    Task<IReadOnlyList<ReviewWhatsAppRequest>> ForDayAsync(DateOnly day, CancellationToken ct = default);

    /// <summary>
    /// Onthoudt dat de gebruiker op de WhatsApp-knop van deze reviewvraag drukte, voor alle reservaties van de klant met
    /// die einddag, elk met een lijn "Reviewvraag via WhatsApp" in de historiek. Moshtar verstuurt zelf niets. Een tweede
    /// klik verandert niets. Gooit een <see cref="InvalidOperationException"/> als de reservatie op deze dag niet in het
    /// blok staat.
    /// </summary>
    Task RecordAsync(DateOnly day, Guid reservationId, ReservationActor actor, CancellationToken ct = default);
}

/// <summary>
/// Eén klant in het blok "Reviewvragen". <see cref="ReservationId"/> en <see cref="Number"/> zijn van zijn eerste
/// reservatie met deze einddag; <see cref="Numbers"/> zijn ze allemaal. <see cref="WhatsAppLink"/>: de click-to-chat-link
/// (wa.me) met het WhatsApp-bericht van het sjabloon Reviewvraag in de taal van de reservatie, null zonder geldig
/// telefoonnummer. <see cref="WhatsAppAt"/>: wanneer op de knop gedrukt werd, of null.
/// </summary>
public record ReviewWhatsAppRequest(
    Guid ReservationId,
    string Number,
    IReadOnlyList<string> Numbers,
    string CustomerName,
    string? Phone,
    DateOnly EndDate,
    string? WhatsAppLink,
    DateTimeOffset? WhatsAppAt);
