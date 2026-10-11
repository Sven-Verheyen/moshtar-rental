using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reservations;

/// <summary>
/// Hoe lang en hoe ver vooruit je kan huren. Langer huurt niemand een springkasteel; een langere of verdere periode
/// is een tikfout, of een poging om de beschikbaarheidsberekening (die elke dag overloopt) lang te laten rekenen.
/// </summary>
public static class RentalPeriodLimits
{
    public const int MaxDays = 31;
    public const int MaxYearsAhead = 2;

    public static bool IsTooLong(DateRange period) => period.Days > MaxDays;

    public static bool IsTooFarAhead(DateOnly start, DateOnly today) => start > today.AddYears(MaxYearsAhead);
}
