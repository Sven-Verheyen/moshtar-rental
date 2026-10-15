namespace Moshtar.Domain.Rounds;

/// <summary>
/// Een nieuwe stop in een bestaande ronde invoegen op de plek met de kleinste extra reistijd. De volgorde van de andere
/// stops blijft zoals ze is, ook als de verhuurder ze zelf koos.
/// </summary>
public static class RoundInsertion
{
    /// <summary>
    /// De plek (0 = eerste stop, n = na de laatste) waar de nieuwe stop het minst reistijd toevoegt. Voor een ronde van
    /// n stops zijn er telkens n + 1 reistijden: <paramref name="legs"/>[i] is het stuk dat bij stop i aankomt (het
    /// laatste: terug naar het bedrijf); <paramref name="toNew"/>[i] van het bedrijf (i = 0) of van stop i − 1 naar de
    /// nieuwe stop; <paramref name="fromNew"/>[i] van de nieuwe stop naar stop i of (het laatste) terug naar het
    /// bedrijf. Bij gelijke extra reistijd wint de vroegste plek.
    /// </summary>
    public static int CheapestPosition(IReadOnlyList<TimeSpan> legs, IReadOnlyList<TimeSpan> toNew, IReadOnlyList<TimeSpan> fromNew)
    {
        if (legs.Count == 0 || toNew.Count != legs.Count || fromNew.Count != legs.Count)
            throw new ArgumentException("Voor een ronde van n stops zijn er telkens n + 1 reistijden nodig.");
        var best = 0;
        var bestExtra = TimeSpan.MaxValue;
        for (var i = 0; i < legs.Count; i++)
        {
            var extra = toNew[i] + fromNew[i] - legs[i];
            if (extra < bestExtra) (best, bestExtra) = (i, extra);
        }
        return best;
    }

    /// <summary>De stops met de nieuwe op <paramref name="position"/>, de andere in dezelfde volgorde.</summary>
    public static IReadOnlyList<T> Insert<T>(IReadOnlyList<T> stops, T stop, int position) =>
        [.. stops.Take(position), stop, .. stops.Skip(position)];
}
