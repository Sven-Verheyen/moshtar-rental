using Moshtar.Domain.Rounds;

namespace Moshtar.Domain.Tests;

/// <summary>
/// Een nieuwe stop in een bestaande ronde komt op de plek met de kleinste extra reistijd, zonder de volgorde van de
/// andere stops te wijzigen.
/// </summary>
public class RoundInsertionTests
{
    private static TimeSpan[] Minutes(params int[] minutes) => minutes.Select(m => TimeSpan.FromMinutes(m)).ToArray();

    [Fact]
    public void The_new_stop_goes_where_it_adds_the_least_travel()
    {
        // Bedrijf → A → B → bedrijf, elk stuk 30 minuten. De nieuwe stop ligt vlak bij A en B, ver van het bedrijf.
        var position = RoundInsertion.CheapestPosition(
            legs: Minutes(30, 30, 30),
            toNew: Minutes(40, 5, 6),
            fromNew: Minutes(5, 6, 40));

        // Tussen A en B: 5 + 6 − 30 = −19; voor A: 40 + 5 − 30 = 15; na B: 6 + 40 − 30 = 16.
        Assert.Equal(1, position);
    }

    [Fact]
    public void A_stop_near_the_business_goes_first_or_last()
    {
        Assert.Equal(0, RoundInsertion.CheapestPosition(Minutes(30, 30, 30), toNew: Minutes(2, 30, 40), fromNew: Minutes(29, 40, 3)));
        Assert.Equal(2, RoundInsertion.CheapestPosition(Minutes(30, 30, 30), toNew: Minutes(3, 40, 29), fromNew: Minutes(40, 30, 2)));
    }

    [Fact]
    public void In_an_empty_round_the_new_stop_is_the_only_one()
    {
        Assert.Equal(0, RoundInsertion.CheapestPosition(Minutes(0), Minutes(10), Minutes(10)));
    }

    [Fact]
    public void Inserting_keeps_the_order_of_the_other_stops()
    {
        Assert.Equal(["A", "B", "nieuw", "C"], RoundInsertion.Insert(["A", "B", "C"], "nieuw", 2));
    }

    [Fact]
    public void Travel_times_that_do_not_match_the_round_are_refused()
    {
        Assert.Throws<ArgumentException>(() => RoundInsertion.CheapestPosition(Minutes(30, 30), Minutes(1, 2, 3), Minutes(1, 2)));
    }
}
