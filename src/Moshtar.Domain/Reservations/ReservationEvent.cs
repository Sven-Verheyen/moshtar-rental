using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

public enum ReservationEventKind
{
    Created = 1,
    StatusChanged = 2,
    Cancelled = 3,
    /// <summary>Het leveradres werd verbeterd, en opnieuw opgezocht op de kaart.</summary>
    AddressCorrected = 4,
    /// <summary>Een gebruiker zette zelf een speld op de kaart voor een adres dat de kaart niet kende.</summary>
    PointConfirmed = 5,
}

/// <summary>Wie een reservatie aanmaakt of wijzigt: een gebruiker van het back office, of de klant via de reservatiewebsite.</summary>
public sealed record ReservationActor(Guid? UserId)
{
    public static readonly ReservationActor Website = new((Guid?)null);
    public static ReservationActor User(Guid userId) => new(userId);
}

/// <summary>Eén regel in de historiek van een reservatie.</summary>
public class ReservationEvent : TenantEntity
{
    public Guid ReservationId { get; set; }
    public ReservationEventKind Kind { get; set; }
    /// <summary>De status van de reservatie na deze gebeurtenis.</summary>
    public ReservationStatus Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>De gebruiker die het deed; leeg als de klant het zelf deed via de reservatiewebsite.</summary>
    public Guid? UserId { get; set; }
}
