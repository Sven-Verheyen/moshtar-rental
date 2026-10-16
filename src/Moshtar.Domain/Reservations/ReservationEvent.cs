using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

public enum ReservationEventKind
{
    Created = 1,
    StatusChanged = 2,
    Cancelled = 3,
    /// <summary>De klant kreeg een mail met alternatieven voor een artikel dat niet verhuurbaar is.</summary>
    AlternativesMailed = 4,
    /// <summary>Een artikel in de reservatie is vervangen door een alternatief, los of binnen een pakket.</summary>
    ItemReplaced = 5,
    /// <summary>Het leveradres werd verbeterd, en opnieuw opgezocht op de kaart.</summary>
    AddressCorrected = 6,
    /// <summary>Een gebruiker zette zelf een speld op de kaart voor een adres dat de kaart niet kende.</summary>
    PointConfirmed = 7,
    /// <summary>De klant kreeg zijn verwacht tijdsvenster voor de levering of ophaling gemaild.</summary>
    ExpectedTimeMailed = 8,
    /// <summary>De klant kreeg de reviewvraag gemaild, de dag na de einddag.</summary>
    ReviewRequestMailed = 9,
    /// <summary>Een gebruiker drukte in de Planning op de WhatsApp-knop van de reviewvraag.</summary>
    ReviewRequestWhatsApp = 10,
}

/// <summary>
/// Wie een reservatie aanmaakt of wijzigt: een gebruiker van het back office, de klant via de reservatiewebsite, of
/// Moshtar zelf (automatisch, bv. de reviewvraag de dag na de einddag).
/// </summary>
public sealed record ReservationActor(Guid? UserId, bool IsAutomatic = false)
{
    public static readonly ReservationActor Website = new((Guid?)null);
    public static readonly ReservationActor Automatic = new(null, IsAutomatic: true);
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
    /// <summary>De gebruiker die het deed; leeg als de klant het zelf deed via de reservatiewebsite of als het automatisch gebeurde.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Moshtar deed het zelf, zonder gebruiker of klant, bv. de reviewvraag mailen.</summary>
    public bool Automatic { get; set; }
    /// <summary>Wat er precies gebeurde, voor de historiek, bv. welke alternatieven gemaild werden; anders null.</summary>
    public string? Note { get; set; }

    public const int MaxNoteLength = 1000;
}
