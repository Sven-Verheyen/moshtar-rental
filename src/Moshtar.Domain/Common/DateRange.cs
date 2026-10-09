namespace Moshtar.Domain.Common;

/// <summary>Huurperiode in hele dagen, begin- en einddag inbegrepen.</summary>
public readonly record struct DateRange
{
    public DateOnly Start { get; }
    public DateOnly End { get; }

    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new ArgumentException("De einddatum mag niet voor de begindatum liggen.", nameof(end));
        Start = start;
        End = end;
    }

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;

    public DateRange Expand(int daysBefore, int daysAfter) =>
        new(Start.AddDays(-daysBefore), End.AddDays(daysAfter));

    public IEnumerable<DateOnly> EachDay()
    {
        for (var d = Start; d <= End; d = d.AddDays(1))
            yield return d;
    }
}
