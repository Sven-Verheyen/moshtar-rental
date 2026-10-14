using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

/// <summary>
/// Een alternatief dat de verhuurder een klant mailde voor een artikel in zijn reservatie dat niet verhuurbaar is, met de
/// meerprijs die hij koos (null: geen meerprijs). Bij het vervangen wordt die meerprijs voorgesteld. Een nieuwe mail voor
/// hetzelfde artikel in dezelfde reservatie vervangt de vorige voorstellen.
/// </summary>
public class AlternativeProposal : TenantEntity
{
    public Guid ReservationId { get; set; }
    /// <summary>Het artikel uit de reservatie dat niet verhuurbaar is.</summary>
    public Guid RentalItemId { get; set; }
    /// <summary>Het voorgestelde artikel.</summary>
    public Guid AlternativeRentalItemId { get; set; }
    public decimal? Surcharge { get; set; }
    /// <summary>De plaats in de mail.</summary>
    public int SortOrder { get; set; }
    public DateTime MailedAtUtc { get; set; }
    /// <summary>De gebruiker die de alternatieven mailde.</summary>
    public Guid? MailedByUserId { get; set; }
}
