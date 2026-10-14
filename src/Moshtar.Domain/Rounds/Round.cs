using Moshtar.Domain.Common;

namespace Moshtar.Domain.Rounds;

/// <summary>De soort ronde: de leverronde ('s morgens) of de ophaalronde ('s avonds).</summary>
public enum RoundKind
{
    Delivery = 0,
    Collection = 1,
}

/// <summary>
/// Een bewaarde ronde: de leveringen of ophalingen van één dag in de volgorde waarin de bestelwagen ze rijdt, vanaf en
/// terug naar het bedrijfsadres. Een ronde bestaat pas zodra iemand een route voorstelt; daarvoor toont de planning de
/// stops op postcode. Stops zonder punt op de kaart horen er niet bij.
/// </summary>
public class Round : TenantEntity
{
    public DateOnly Day { get; set; }
    public RoundKind Kind { get; set; }

    /// <summary>Het vertrekuur van deze dag; bij het voorstellen het standaarduur uit de instellingen.</summary>
    public TimeOnly Departure { get; set; }

    /// <summary>De reistijd van de laatste stop terug naar het bedrijf, in minuten.</summary>
    public int ReturnTravelMinutes { get; set; }

    /// <summary>Wanneer de bestelwagen terug is, berekend.</summary>
    public TimeOnly Return { get; set; }

    /// <summary>
    /// Loopt de ronde over middernacht? Dan valt de terugkeer (op de klok) voor het vertrek; een ronde duurt nooit
    /// een hele dag.
    /// </summary>
    public bool PastMidnight => Return < Departure;

    /// <summary>De stops, in de volgorde van <see cref="RoundStop.Position"/>.</summary>
    public List<RoundStop> Stops { get; set; } = [];

    /// <summary>De stops in rijvolgorde.</summary>
    public IReadOnlyList<RoundStop> Ordered => Stops.OrderBy(s => s.Position).ToList();

    /// <summary>
    /// Rekent de uren opnieuw uit met de bewaarde reistijden (zie <see cref="RoundTimes"/>) en nummert de stops in de
    /// gegeven volgorde. <paramref name="defaultHandlingMinutes"/> is de opbouw- of afbouwtijd van een stop zonder eigen tijd.
    /// </summary>
    public void Recalculate(int defaultHandlingMinutes, int windowMinutes)
    {
        var ordered = Ordered;
        var times = RoundTimes.Calculate(
            Departure,
            ordered.Select(s => new RoundTimesStop(
                TimeSpan.FromMinutes(s.TravelMinutes),
                TimeSpan.FromMinutes(s.HandlingMinutes ?? defaultHandlingMinutes),
                s.FixedTime)).ToList(),
            TimeSpan.FromMinutes(ReturnTravelMinutes),
            windowMinutes);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i + 1;
            ordered[i].Arrival = times.Stops[i].Arrival;
            ordered[i].Window = times.Stops[i].Window;
        }
        Return = times.Return;
    }
}

/// <summary>
/// Eén stop van een ronde: een reservatie, met wat de verhuurder voor die stop koos (vast uur, eigen opbouw- of
/// afbouwtijd), wat berekend werd (reistijd, aankomst, venster) en wat de klant al kreeg (gemaild venster, tijdstip
/// van de mail en van de WhatsApp-knop).
/// </summary>
public class RoundStop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReservationId { get; set; }

    /// <summary>De plaats in de ronde, vanaf 1.</summary>
    public int Position { get; set; }

    /// <summary>Het punt waarmee de route berekend werd; is het punt van de reservatie intussen anders, dan is de ronde gewijzigd.</summary>
    public GeoPoint Point { get; set; } = default!;

    /// <summary>Het uur waarop de verhuurder er moet zijn; vroeger aankomen is wachten, later is een waarschuwing.</summary>
    public TimeOnly? FixedTime { get; set; }

    /// <summary>De eigen opbouw- of afbouwtijd van deze stop in minuten; leeg: de standaard uit de instellingen.</summary>
    public int? HandlingMinutes { get; set; }

    /// <summary>De reistijd van de vorige stop (of van het bedrijf) naar deze stop, in minuten.</summary>
    public int TravelMinutes { get; set; }

    /// <summary>Het berekende aankomstuur.</summary>
    public TimeOnly Arrival { get; set; }

    public TimeOnly WindowStart { get; set; }
    public TimeOnly WindowEnd { get; set; }

    /// <summary>Het berekende verwacht tijdsvenster.</summary>
    public ExpectedWindow Window
    {
        get => new(WindowStart, WindowEnd);
        set => (WindowStart, WindowEnd) = (value.Start, value.End);
    }

    /// <summary>Is de verhuurder er pas na het vaste uur?</summary>
    public bool Late => FixedTime is { } fixedTime && Arrival > fixedTime;

    /// <summary>Het venster dat de klant laatst gemaild kreeg, en wanneer.</summary>
    public TimeOnly? MailedWindowStart { get; set; }
    public TimeOnly? MailedWindowEnd { get; set; }
    public DateTimeOffset? MailedAt { get; set; }

    /// <summary>Wanneer laatst op de WhatsApp-knop van deze stop gedrukt werd.</summary>
    public DateTimeOffset? WhatsAppAt { get; set; }
}
