using System.Globalization;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Site;

/// <summary>
/// De huurperiode die de klant koos met "Kies je datum", uit de querystring: <c>?van=2026-06-12&amp;tot=2026-06-13</c>.
/// Zonder einddag is het dezelfde dag. Een pagina met een huurperiode in de URL wordt niet geïndexeerd.
/// </summary>
public sealed record ChosenPeriod
{
    public const string StartKey = "van";
    public const string EndKey = "tot";

    /// <summary>De gekozen huurperiode, of null als er geen (geldige) gekozen is.</summary>
    public DateRange? Period { get; private init; }

    /// <summary>Of er een datum in de URL staat, ook een ongeldige.</summary>
    public bool InUrl { get; private init; }

    /// <summary>De resourcesleutel van de melding als de datum niet kan, bv. een einddag voor de begindag.</summary>
    public string? Problem { get; private init; }

    /// <summary>De datums zoals ze in het formulier terugkomen, ook als ze niet kloppen.</summary>
    public DateOnly? Start { get; private init; }
    public DateOnly? End { get; private init; }

    public static readonly ChosenPeriod None = new();

    public static ChosenPeriod From(string? start, string? end, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end)) return None;
        var startDay = Parse(start);
        var endDay = string.IsNullOrWhiteSpace(end) ? startDay : Parse(end);
        var problem =
            startDay is null || endDay is null ? "PeriodUnreadable"
            : endDay < startDay ? "PeriodEndBeforeStart"
            : startDay < today ? "PeriodInPast"
            : RentalPeriodLimits.IsTooFarAhead(startDay.Value, today) ? "PeriodTooFarAhead"
            : RentalPeriodLimits.IsTooLong(new DateRange(startDay.Value, endDay.Value)) ? "PeriodTooLong"
            : null;
        return new ChosenPeriod
        {
            InUrl = true,
            Problem = problem,
            Start = startDay,
            End = endDay,
            Period = problem is null ? new DateRange(startDay!.Value, endDay!.Value) : null,
        };
    }

    /// <summary>De querystring om de huurperiode mee te nemen naar een andere pagina, of "" zonder geldige huurperiode.</summary>
    public string Query => Period is { } p ? $"?{StartKey}={Format(p.Start)}&{EndKey}={Format(p.End)}" : "";

    /// <summary>Vandaag bij de verhuurder; een dag in het verleden kan je niet meer reserveren.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public static string Format(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
