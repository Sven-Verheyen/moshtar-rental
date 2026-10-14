using System.Globalization;

namespace Moshtar.Domain.Common;

/// <summary>
/// De klok en de taal van het back office: de verhuurders zitten in België, dus "vandaag" en een uur van de dag zijn
/// Belgische tijd, ook op een server die in UTC draait.
/// </summary>
public static class Belgium
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>Het Nederlands van België, voor datums zoals "woensdag 11 november 2026".</summary>
    public static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-BE");

    /// <summary>De datum van vandaag in België.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Local(clock.GetUtcNow()).DateTime);

    /// <summary>Een tijdstip in Belgische tijd.</summary>
    public static DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TimeZone);

    /// <summary>Dat uur op die dag, in Belgische tijd (zomer- of wintertijd van die dag).</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}
