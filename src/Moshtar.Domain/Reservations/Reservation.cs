using Moshtar.Domain.Catalog;
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
    /// <summary>
    /// Het punt van het leveradres op de kaart, of null zolang het niet gevonden is. Zonder punt toont de planning
    /// een waarschuwing en kan de stop niet mee in een route.
    /// </summary>
    public GeoPoint? DeliveryPoint { get; set; }
    /// <summary>Zette een gebruiker het punt zelf, met een speld op de kaart, omdat de kaart het adres niet kende?</summary>
    public bool DeliveryPointConfirmed { get; set; }
    public string? Notes { get; set; }
    public string Culture { get; set; } = "nl";

    public decimal TotalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Wanneer de klant het laatst een mail met alternatieven kreeg, of null.</summary>
    public DateTime? AlternativesMailedAtUtc { get; set; }

    /// <summary>
    /// Wanneer de reviewvraag voor deze reservatie vertrok, of null. Een reservatie krijgt er hoogstens één: dit tijdstip
    /// wordt gezet voor de mail vertrekt, zodat een herstart of een tweede run nooit dubbel stuurt.
    /// </summary>
    public DateTime? ReviewRequestMailedAtUtc { get; set; }

    /// <summary>
    /// Wanneer een gebruiker in de Planning op de WhatsApp-knop van de reviewvraag drukte, of null. Moshtar verstuurt
    /// zelf niets: de gebruiker verstuurt het ingevulde bericht vanaf het nummer van de verhuurder.
    /// </summary>
    public DateTime? ReviewRequestWhatsAppAtUtc { get; set; }

    public List<ReservationLine> Lines { get; set; } = [];

    /// <summary>Wie wat deed met deze reservatie, en wanneer.</summary>
    public List<ReservationEvent> History { get; set; } = [];

    /// <summary>Noteert in de historiek dat de reservatie aangemaakt is.</summary>
    public void RecordCreated(ReservationActor actor, DateTime atUtc) =>
        Record(ReservationEventKind.Created, actor, atUtc);

    /// <summary>Noteert op de reservatie en in de historiek dat de klant een mail met alternatieven kreeg.</summary>
    /// <param name="note">Welke alternatieven, met hun meerprijs, voor de historiek.</param>
    public void RecordAlternativesMailed(ReservationActor actor, DateTime atUtc, string note)
    {
        AlternativesMailedAtUtc = atUtc;
        Record(ReservationEventKind.AlternativesMailed, actor, atUtc, note);
    }

    /// <summary>
    /// Vervangt een artikel door een alternatief, overal waar de reservatie het gebruikt: op een losse lijn wordt het
    /// artikel vervangen met behoud van prijs, in een gereserveerd pakket enkel binnen het pakket voor deze reservatie
    /// (de pakketprijs blijft). Een meerprijs komt als aparte lijn bij het totaal. Noteert het in de historiek.
    /// </summary>
    /// <param name="bundles">De gereserveerde pakketten, om te weten welke het artikel bevatten.</param>
    /// <param name="note">Wat door wat vervangen werd en de meerprijs, voor de historiek.</param>
    public void ReplaceItem(ItemReplacement replacement, IReadOnlyDictionary<Guid, Bundle> bundles, ReservationActor actor, DateTime atUtc, string note)
    {
        if (replacement.RentalItemId == replacement.ReplacementRentalItemId)
            throw new ArgumentException("Een artikel kan niet door zichzelf vervangen worden.");
        if (replacement.Surcharge < 0)
            throw new ArgumentException("Een meerprijs kan niet negatief zijn.");
        if (!OccupiesStock)
            throw new InvalidOperationException($"Reservatie {Number} is {(Status == ReservationStatus.Cancelled ? "geannuleerd" : "afgerond")}: er valt niets meer te vervangen.");

        var found = false;
        foreach (var line in Lines)
        {
            if (line.RentalItemId == replacement.RentalItemId)
            {
                line.RentalItemId = replacement.ReplacementRentalItemId;
                line.Description = replacement.ReplacementName;
                found = true;
            }
            else if (line.BundleId is { } bundleId && bundles.TryGetValue(bundleId, out var bundle))
            {
                found |= line.ReplaceInBundle(bundle, replacement);
            }
        }
        if (!found)
            throw new ArgumentException("Dit artikel zit niet in de reservatie.");

        if (replacement.Surcharge is > 0 and var surcharge)
        {
            Lines.Add(new ReservationLine { Quantity = 1, Description = replacement.SurchargeDescription, UnitPrice = surcharge });
            TotalPrice += surcharge;
        }
        Record(ReservationEventKind.ItemReplaced, actor, atUtc, note);
    }

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

    /// <summary>
    /// Verbetert het leveradres, met het punt dat de kaart er nu voor vindt (null: nog altijd niet gevonden), en noteert
    /// dat in de historiek. Een speld die eerder bevestigd werd, hoorde bij het oude adres en vervalt.
    /// </summary>
    public void CorrectDeliveryAddress(Address address, GeoPoint? point, ReservationActor actor, DateTime atUtc)
    {
        if (DeliveryMethod != DeliveryMethod.Delivery)
            throw new InvalidOperationException($"Reservatie {Number} wordt afgehaald en heeft geen leveradres.");
        DeliveryAddress = address;
        DeliveryPoint = point;
        DeliveryPointConfirmed = false;
        Record(ReservationEventKind.AddressCorrected, actor, atUtc);
    }

    /// <summary>
    /// Een gebruiker bevestigt dat het leveradres juist is door zelf een speld op de kaart te zetten. Vanaf dan telt
    /// de stop als gevonden.
    /// </summary>
    public void ConfirmDeliveryPoint(GeoPoint point, ReservationActor actor, DateTime atUtc)
    {
        if (DeliveryMethod != DeliveryMethod.Delivery)
            throw new InvalidOperationException($"Reservatie {Number} wordt afgehaald en heeft geen leveradres.");
        DeliveryPoint = point;
        DeliveryPointConfirmed = true;
        Record(ReservationEventKind.PointConfirmed, actor, atUtc);
    }

    /// <summary>Noteert in de historiek dat de klant zijn verwacht tijdsvenster gemaild kreeg.</summary>
    public void RecordExpectedTimeMailed(ReservationActor actor, DateTime atUtc) =>
        Record(ReservationEventKind.ExpectedTimeMailed, actor, atUtc);

    /// <summary>Noteert op de reservatie en in de historiek dat de klant de reviewvraag gemaild kreeg.</summary>
    public void RecordReviewRequestMailed(DateTime atUtc)
    {
        ReviewRequestMailedAtUtc = atUtc;
        Record(ReservationEventKind.ReviewRequestMailed, ReservationActor.Automatic, atUtc);
    }

    /// <summary>Noteert op de reservatie en in de historiek dat een gebruiker de reviewvraag via WhatsApp stuurde.</summary>
    public void RecordReviewRequestWhatsApp(ReservationActor actor, DateTime atUtc)
    {
        ReviewRequestWhatsAppAtUtc = atUtc;
        Record(ReservationEventKind.ReviewRequestWhatsApp, actor, atUtc);
    }

    private void Record(ReservationEventKind kind, ReservationActor actor, DateTime atUtc, string? note = null) =>
        History.Add(new ReservationEvent
        {
            ReservationId = Id, Kind = kind, Status = Status, OccurredAtUtc = atUtc, UserId = actor.UserId, Automatic = actor.IsAutomatic,
            Note = note is { Length: > ReservationEvent.MaxNoteLength } ? note[..(ReservationEvent.MaxNoteLength - 1)] + "…" : note,
        });

    /// <summary>De statussen waarin een reservatie voorraad bezet: niet geannuleerd en niet afgerond.</summary>
    public static readonly IReadOnlyCollection<ReservationStatus> StockOccupyingStatuses =
        [ReservationStatus.Confirmed, ReservationStatus.Delivered, ReservationStatus.Returned];

    /// <summary>Telt deze reservatie mee voor de bezetting van de voorraad?</summary>
    public bool OccupiesStock => StockOccupyingStatuses.Contains(Status);
}

/// <summary>
/// Een lijn is een los artikel, een pakket, of een prijslijn zonder artikel of pakket: een meerprijs bij het vervangen
/// (positieve prijs) of een korting van een prijsregel (negatieve prijs, zie <see cref="IsDiscount"/>).
/// </summary>
public class ReservationLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RentalItemId { get; set; }
    public Guid? BundleId { get; set; }
    public int Quantity { get; set; } = 1;
    public string Description { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>
    /// Een prijslijn zonder artikel of pakket (meerprijs of korting): er gaat niets mee naar de klant, dus ze staat
    /// niet in de Planning of op de Leverlijst.
    /// </summary>
    public bool IsPriceOnly => RentalItemId is null && BundleId is null;

    /// <summary>
    /// Een kortingslijn: wat een prijsregel (combinatiekorting, actie) van het totaal aftrekt, met de naam van de regel
    /// als omschrijving. Zonder artikel of pakket, aantal 1 en een negatieve prijs.
    /// </summary>
    public bool IsDiscount => IsPriceOnly && UnitPrice < 0;

    /// <summary>Op een pakketlijn: de artikelen van het pakket die voor deze reservatie door een ander vervangen zijn.</summary>
    public List<BundleItemReplacement> Replacements { get; set; } = [];

    /// <summary>Het artikel dat de reservatie gebruikt voor dit artikel uit het pakket: het vervangende, of het artikel zelf.</summary>
    public Guid ItemInBundle(Guid bundleItemId) =>
        Replacements.FirstOrDefault(r => r.RentalItemId == bundleItemId)?.ReplacementRentalItemId ?? bundleItemId;

    /// <summary>Vervangt het artikel binnen het pakket, als het pakket het (nog) gebruikt.</summary>
    internal bool ReplaceInBundle(Bundle bundle, ItemReplacement replacement)
    {
        var replaced = Replacements.FirstOrDefault(r => r.ReplacementRentalItemId == replacement.RentalItemId);
        if (replaced is not null)
        {
            // Al eens vervangen: de notitie blijft vertrekken van het artikel uit het pakket.
            if (replaced.RentalItemId == replacement.ReplacementRentalItemId)
                Replacements.Remove(replaced);
            else
                (replaced.ReplacementRentalItemId, replaced.ReplacementName) = (replacement.ReplacementRentalItemId, replacement.ReplacementName);
            return true;
        }
        if (!bundle.Items.Any(i => i.RentalItemId == replacement.RentalItemId) || Replacements.Any(r => r.RentalItemId == replacement.RentalItemId))
            return false;
        Replacements.Add(new BundleItemReplacement
        {
            RentalItemId = replacement.RentalItemId,
            RentalItemName = replacement.Name,
            ReplacementRentalItemId = replacement.ReplacementRentalItemId,
            ReplacementName = replacement.ReplacementName,
        });
        return true;
    }
}

/// <summary>
/// Een artikel uit een gereserveerd pakket dat voor deze reservatie door een alternatief vervangen is, bv. "Jungle
/// vervangen door Piraat". De namen staan in de taal van de reservatie, zoals de omschrijving van de lijn.
/// </summary>
public class BundleItemReplacement
{
    /// <summary>Het artikel uit het pakket.</summary>
    public Guid RentalItemId { get; set; }
    public string RentalItemName { get; set; } = "";
    /// <summary>Het artikel dat de reservatie in de plaats gebruikt.</summary>
    public Guid ReplacementRentalItemId { get; set; }
    public string ReplacementName { get; set; } = "";
}

/// <summary>
/// Vervang <see cref="RentalItemId"/> door <see cref="ReplacementRentalItemId"/>, met de namen in de taal van de
/// reservatie en eventueel een meerprijs (null of 0: geen) met de omschrijving van zijn lijn.
/// </summary>
public sealed record ItemReplacement(
    Guid RentalItemId,
    string Name,
    Guid ReplacementRentalItemId,
    string ReplacementName,
    decimal? Surcharge = null,
    string SurchargeDescription = "");
