namespace Moshtar.Domain.Rounds;

/// <summary>
/// Een stop zoals de berekening van de uren ze ziet: de reistijd vanaf de vorige stop (of vanaf het bedrijf), de opbouw-
/// of afbouwtijd, en eventueel het vaste uur waarop de verhuurder er moet zijn.
/// </summary>
public sealed record RoundTimesStop(TimeSpan Travel, TimeSpan Handling, TimeOnly? FixedTime = null);

/// <summary>
/// Het aankomstuur bij een stop en het verwacht tijdsvenster dat de klant krijgt. <see cref="Late"/>: de stop heeft een
/// vast uur en de verhuurder is er pas later; dan telt het echte aankomstuur.
/// </summary>
public sealed record StopTimes(TimeOnly Arrival, ExpectedWindow Window, bool Late);

/// <summary>De uren van een ronde: per stop, in volgorde, en wanneer de bestelwagen terug is.</summary>
public sealed record RoundTimesResult(IReadOnlyList<StopTimes> Stops, TimeOnly Return);

/// <summary>
/// Het verwacht tijdsvenster: het tijdsvak waarin de verhuurder verwacht bij de klant aan te komen,
/// zoals "9u30 – 10u30".
/// </summary>
public sealed record ExpectedWindow(TimeOnly Start, TimeOnly End)
{
    public override string ToString() => $"{Time(Start)} – {Time(End)}";

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
        var clock = departure;
        var result = new List<StopTimes>(stops.Count);
        foreach (var stop in stops)
        {
            var arrival = clock.Add(stop.Travel);
            var late = false;
            if (stop.FixedTime is { } fixedTime)
            {
                if (arrival < fixedTime) arrival = fixedTime;
                else late = arrival > fixedTime;
            }
            result.Add(new StopTimes(arrival, Window(arrival, windowMinutes), late));
            clock = arrival.Add(stop.Handling);
        }
        return new RoundTimesResult(result, clock.Add(returnTravel));
    }

    /// <summary>Het venster rond een aankomst, beginnend op het dichtstbije halve uur (bij gelijke afstand het latere).</summary>
    public static ExpectedWindow Window(TimeOnly arrival, int windowMinutes)
    {
        var centredStart = (int)arrival.ToTimeSpan().TotalMinutes - windowMinutes / 2;
        var start = (int)Math.Floor((centredStart + 15) / 30.0) * 30;
        return new ExpectedWindow(MinutesOfDay(start), MinutesOfDay(start + windowMinutes));
    }

    private static TimeOnly MinutesOfDay(int minutes) => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(((minutes % 1440) + 1440) % 1440));
}
