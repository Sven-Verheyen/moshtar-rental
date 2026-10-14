namespace Moshtar.Web.Components.Admin;

/// <summary>Hoe de Planning getoond wordt: één dag in detail, een week of een maand met tellers.</summary>
public enum PlanningView
{
    Day,
    Week,
    Month,
}

public static class PlanningViews
{
    /// <summary>De weergave uit de querystring (dag, week, maand); onbekend of leeg is de dag.</summary>
    public static PlanningView Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "week" => PlanningView.Week,
        "maand" => PlanningView.Month,
        _ => PlanningView.Day,
    };

    /// <summary>De link naar de Planning in deze weergave rond deze datum. De dag heeft geen weergave in de link.</summary>
    public static string Url(PlanningView view, DateOnly day) => view switch
    {
        PlanningView.Week => $"admin/planning?weergave=week&dag={day:yyyy-MM-dd}",
        PlanningView.Month => $"admin/planning?weergave=maand&dag={day:yyyy-MM-dd}",
        _ => $"admin/planning?dag={day:yyyy-MM-dd}",
    };
}
