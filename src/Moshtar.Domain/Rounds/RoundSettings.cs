namespace Moshtar.Domain.Rounds;

/// <summary>
/// De standaarden van een verhuurder voor zijn rondes, in Bedrijfsgegevens: de vertrekuren, hoe lang opbouwen en
/// afbouwen duurt, en hoe breed het verwacht tijdsvenster is.
/// </summary>
public sealed record RoundSettings(
    TimeOnly DeliveryDeparture,
    TimeOnly CollectionDeparture,
    int SetupMinutes,
    int TeardownMinutes,
    int WindowMinutes)
{
    public static readonly RoundSettings Default = new(new TimeOnly(8, 0), new TimeOnly(18, 0), 30, 20, 60);

    /// <summary>Het standaard vertrekuur van deze soort ronde.</summary>
    public TimeOnly Departure(RoundKind kind) => kind == RoundKind.Delivery ? DeliveryDeparture : CollectionDeparture;

    /// <summary>Opbouwen bij een levering, afbouwen bij een ophaling.</summary>
    public int HandlingMinutes(RoundKind kind) => kind == RoundKind.Delivery ? SetupMinutes : TeardownMinutes;

    /// <summary>Wat er mis is met deze instellingen; leeg als alles klopt.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!IsHandlingTime(SetupMinutes)) problems.Add("De opbouwtijd moet tussen 0 en 480 minuten liggen.");
        if (!IsHandlingTime(TeardownMinutes)) problems.Add("De afbouwtijd moet tussen 0 en 480 minuten liggen.");
        // Een veelvoud van 30: zo eindigt een venster dat op een half uur begint ook op een half uur.
        if (WindowMinutes is < 30 or > 240 || WindowMinutes % 30 != 0)
            problems.Add("De breedte van het venster is een veelvoud van 30 minuten, van 30 tot 240 minuten.");
        return problems;
    }

    /// <summary>Een eigen opbouw- of afbouwtijd van één stop mag even lang zijn als de standaard.</summary>
    public static bool IsHandlingTime(int minutes) => minutes is >= 0 and <= 480;
}
