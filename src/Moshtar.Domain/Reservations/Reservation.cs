using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;

namespace Moshtar.Domain.Reservations;

public enum ReservationStatus
{
    Confirmed = 1,
    Delivered = 2,
    Returned = 3,
    Completed = 4,
    Cancelled = 9,
}

public enum DeliveryMethod
{
    Delivery = 1,
    Pickup = 2,
}

public class Reservation : TenantEntity
{
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateRange Period => new(StartDate, EndDate);

    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    public Address? DeliveryAddress { get; set; }
    public string? Notes { get; set; }
    public string Culture { get; set; } = "nl";

    public decimal TotalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ReservationLine> Lines { get; set; } = [];

    /// <summary>Wie wat deed met deze reservatie, en wanneer.</summary>
    public List<ReservationEvent> History { get; set; } = [];

    /// <summary>Noteert in de historiek dat de reservatie aangemaakt is.</summary>
    public void RecordCreated(ReservationActor actor, DateTime atUtc) =>
        Record(ReservationEventKind.Created, actor, atUtc);

    /// <summary>
    /// Zet de reservatie in een nieuwe status en noteert dat in de historiek. Een geannuleerde reservatie
    /// blijft geannuleerd: haar voorraad kan intussen aan iemand anders verhuurd zijn.
    /// </summary>
    public void ChangeStatus(ReservationStatus status, ReservationActor actor, DateTime atUtc)
    {
        if (status == Status) return;
        if (Status == ReservationStatus.Cancelled)
            throw new InvalidOperationException($"Reservatie {Number} is geannuleerd en kan niet meer van status veranderen.");
        Status = status;
        Record(status == ReservationStatus.Cancelled ? ReservationEventKind.Cancelled : ReservationEventKind.StatusChanged, actor, atUtc);
    }

    private void Record(ReservationEventKind kind, ReservationActor actor, DateTime atUtc) =>
        History.Add(new ReservationEvent { ReservationId = Id, Kind = kind, Status = Status, OccurredAtUtc = atUtc, UserId = actor.UserId });

    /// <summary>Telt deze reservatie mee voor de bezetting van de voorraad?</summary>
    public bool OccupiesStock => Status is not ReservationStatus.Cancelled and not ReservationStatus.Completed;
}

/// <summary>Een lijn is ofwel een los artikel, ofwel een pakket.</summary>
public class ReservationLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RentalItemId { get; set; }
    public Guid? BundleId { get; set; }
    public int Quantity { get; set; } = 1;
    public string Description { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}
