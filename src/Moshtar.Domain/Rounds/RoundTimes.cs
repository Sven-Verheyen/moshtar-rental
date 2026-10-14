namespace Moshtar.Domain.Rounds;

/// <summary>
/// Een stop zoals de berekening van de uren ze ziet: de reistijd vanaf de vorige stop (of vanaf het bedrijf), de opbouw-
/// of afbouwtijd, en eventueel het vaste uur waarop de verhuurder er moet zijn.
/// </summary>
public sealed record RoundTimesStop(TimeSpan Travel, TimeSpan Handling, TimeOnly? FixedTime = null);

/// <summary>
/// Het aankomstuur bij een stop en het verwacht tijdsvenster dat de klant krijgt. <see cref="Late"/>: de stop heeft een
/// vast uur en de verhuurder is er pas later; dan telt het echte aankomstuur. <see cref="PastMidnight"/>: het venster
/// loopt over middernacht of ligt erna, dus niet (helemaal) op de dag van de ronde.
/// </summary>
public sealed record StopTimes(TimeOnly Arrival, ExpectedWindow Window, bool Late, bool PastMidnight = false);

/// <summary>
/// De uren van een ronde: per stop, in volgorde, en wanneer de bestelwagen terug is. <see cref="PastMidnight"/>: de
/// ronde loopt over middernacht; de uren na middernacht zijn dan uren van de volgende dag.
/// </summary>
public sealed record RoundTimesResult(IReadOnlyList<StopTimes> Stops, TimeOnly Return, bool PastMidnight = false);

/// <summary>
/// Het verwacht tijdsvenster: het tijdsvak waarin de verhuurder verwacht bij de klant aan te komen,
/// zoals "9u30 – 10u30".
/// </summary>
public sealed record ExpectedWindow(TimeOnly Start, TimeOnly End)
{
    public override string ToString() => $"{Time(Start)} – {Time(End)}";

    /// <summary>
    /// Het venster zoals een klant het leest in zijn taal: 9u30 – 10u30, 9h30 – 10h30 of 9:30 – 10:30. Een andere taal
    /// krijgt de Vlaamse schrijfwijze.
    /// </summary>
    public string ToString(string language)
    {
        var separator = language switch { "fr" => "h", "en" => ":", _ => "u" };
        return $"{Write(Start)} – {Write(End)}";

        string Write(TimeOnly time) => $"{time.Hour}{separator}{time.Minute:00}";
    }

    /// <summary>Een uur zoals in Vlaanderen geschreven: 9u30, 18u00.</summary>
    public static string Time(TimeOnly time) => $"{time.Hour}u{time.Minute:00}";
}

/// <summary>
/// Rekent de uren van een ronde uit. Puur: dezelfde vertrektijd, reistijden en stops geven altijd dezelfde uren.
/// </summary>
public static class RoundTimes
{
    /// <summary>
    /// De aankomst bij elke stop: vertrekken, rijden, opbouwen of afbouwen, en naar de volgende rijden. Bij een stop met
    /// een vast uur wacht de verhuurder tot dat uur als hij vroeger is; is hij later, dan is de stop te laat. Het venster
    /// is <paramref name="windowMinutes"/> breed, gecentreerd rond de aankomst en afgerond zodat het op een heel of half
    /// uur begint.
    /// </summary>
    public static RoundTimesResult Calculate(TimeOnly departure, IReadOnlyList<RoundTimesStop> stops, TimeSpan returnTravel, int windowMinutes)
    {
        // Gerekend vanaf middernacht van de dag zelf, zodat een uur na middernacht niet terug naar 0u00 springt.
        var clock = departure.ToTimeSpan();
        var result = new List<StopTimes>(stops.Count);
        foreach (var stop in stops)
        {
            var arrival = clock + stop.Travel;
            var late = false;
            if (stop.FixedTime is { } fixedTime)
            {
                var fixedAt = fixedTime.ToTimeSpan();
                if (arrival < fixedAt) arrival = fixedAt;
                else late = arrival > fixedAt;
            }
            var arrivalTime = TimeOnly.FromTimeSpan(OfDay(arrival));
            var windowStart = WindowStartMinutes(arrival, windowMinutes);
            var pastMidnight = windowStart < 0 || windowStart + windowMinutes > MinutesPerDay;
            result.Add(new StopTimes(arrivalTime, Window(arrivalTime, windowMinutes), late, pastMidnight));
            clock = arrival + stop.Handling;
        }
        var back = clock + returnTravel;
        return new RoundTimesResult(result, TimeOnly.FromTimeSpan(OfDay(back)), PastMidnight: back >= TimeSpan.FromDays(1));
    }

    /// <summary>Het venster rond een aankomst, beginnend op het dichtstbije halve uur (bij gelijke afstand het latere).</summary>
    public static ExpectedWindow Window(TimeOnly arrival, int windowMinutes)
    {
        var start = WindowStartMinutes(arrival.ToTimeSpan(), windowMinutes);
        return new ExpectedWindow(MinutesOfDay(start), MinutesOfDay(start + windowMinutes));
    }

    private const int MinutesPerDay = 24 * 60;

    /// <summary>Het begin van het venster in minuten na middernacht van de dag van de ronde (na 24u00: de volgende dag).</summary>
    private static int WindowStartMinutes(TimeSpan arrival, int windowMinutes)
    {
        var centredStart = (int)arrival.TotalMinutes - windowMinutes / 2;
        return (int)Math.Floor((centredStart + 15) / 30.0) * 30;
    }

    private static TimeSpan OfDay(TimeSpan time) => TimeSpan.FromTicks(time.Ticks % TimeSpan.TicksPerDay);

    private static TimeOnly MinutesOfDay(int minutes) => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(((minutes % 1440) + 1440) % 1440));
}
