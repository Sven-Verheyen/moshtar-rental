using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Reservations;

/// <summary>
/// Een alternatief voor een artikel in een getroffen reservatie: een artikel uit dezelfde categorie, aan, en vrij in de
/// volledige huurperiode voor het nodige aantal.
/// </summary>
/// <param name="Name">De naam in de standaardtaal van de verhuurder, voor het back office.</param>
/// <param name="PhotoUrl">Het publieke adres van de hoofdfoto, of null zonder foto.</param>
/// <param name="PriceDifference">
/// Hulp bij de meerprijs: wat het alternatief voor de huurperiode en het nodige aantal meer (of minder) kost dan het
/// artikel uit de reservatie, aan artikelprijs, ook als dat artikel in een pakket zit.
/// </param>
/// <param name="Surcharge">De meerprijs die de laatste keer voor dit alternatief gemaild werd, of null (geen meerprijs).</param>
public sealed record Alternative(Guid RentalItemId, string Name, string? PhotoUrl, decimal PriceDifference, decimal? Surcharge);

/// <summary>Een aangevinkt alternatief, met de meerprijs die de klant betaalt als hij het kiest (null of 0: geen meerprijs).</summary>
public sealed record ChosenAlternative(Guid RentalItemId, decimal? Surcharge);

/// <summary>
/// Een alternatief dat aan de klant gemaild werd voor <see cref="RentalItemId"/> in zijn reservatie, met de gekozen
/// meerprijs (null: geen meerprijs), om bij het vervangen te gebruiken.
/// </summary>
public sealed record ProposedAlternative(Guid RentalItemId, Guid AlternativeRentalItemId, decimal? Surcharge, DateTime MailedAtUtc);

/// <summary>Wanneer en door wie de alternatieven voor een reservatie het laatst gemaild werden.</summary>
public sealed record AlternativesMailed(DateTime AtUtc, string? UserEmail);

/// <summary>
/// De alternatieven voor een getroffen reservatie (Tekorten), en de mail die ze de klant voorstelt in zijn eigen taal,
/// voor Beheerder en Medewerker.
/// </summary>
public interface IReservationAlternatives
{
    /// <summary>De alternatieven voor dit artikel in deze reservatie, in de volgorde van de categorie.</summary>
    Task<IReadOnlyList<Alternative>> GetAsync(Guid reservationId, Guid rentalItemId, CancellationToken ct = default);

    /// <summary>
    /// Mailt de klant de gekozen alternatieven voor dit artikel, met hun meerprijs, en bewaart het tijdstip op de
    /// reservatie, in de historiek en de meerprijzen voor het vervangen (een vorige keer voor dit artikel wordt vervangen).
    /// Gooit een <see cref="ArgumentException"/> als er niets gekozen is, een meerprijs negatief is of een gekozen
    /// artikel geen (vrij) alternatief meer is; dan vertrekt er niets.
    /// </summary>
    Task MailAsync(Guid reservationId, Guid rentalItemId, IReadOnlyList<ChosenAlternative> chosen, ReservationActor actor, CancellationToken ct = default);

    /// <summary>De alternatieven die voor deze reservatie gemaild werden, met hun meerprijs.</summary>
    Task<IReadOnlyList<ProposedAlternative>> GetProposedAsync(Guid reservationId, CancellationToken ct = default);
}
