using Moshtar.Domain.Rounds;

namespace Moshtar.Domain.Tests;

/// <summary>
/// Aankomsturen en verwachte tijdsvensters van een ronde: vertrekuur, reistijden, opbouw- of afbouwtijd per stop en
/// vaste uren.
/// </summary>
public class RoundTimesTests
{
    private static TimeSpan Minutes(int minutes) => TimeSpan.FromMinutes(minutes);

    private static RoundTimesStop Stop(int travel, int handling = 30, TimeOnly? fixedTime = null) =>
        new(Minutes(travel), Minutes(handling), fixedTime);

    [Fact]
    public void Each_stop_is_reached_after_the_travel_and_the_setup_of_the_stops_before_it()
    {
        var times = RoundTimes.Calculate(new TimeOnly(8, 0), [Stop(20), Stop(15, handling: 45), Stop(10)], returnTravel: Minutes(25), windowMinutes: 60);

        Assert.Equal([new TimeOnly(8, 20), new TimeOnly(9, 5), new TimeOnly(10, 0)], times.Stops.Select(s => s.Arrival));
        Assert.Equal(new TimeOnly(10, 55), times.Return);
    }

    [Theory]
    [InlineData(9, 47, 9, 30, 10, 30)]
    [InlineData(10, 0, 9, 30, 10, 30)]
    [InlineData(10, 14, 9, 30, 10, 30)]
    [InlineData(10, 15, 10, 0, 11, 0)]
    [InlineData(10, 16, 10, 0, 11, 0)]
    public void The_window_is_centred_on_the_arrival_and_starts_on_a_half_hour(int hour, int minute, int fromHour, int fromMinute, int toHour, int toMinute)
    {
        var departure = new TimeOnly(hour, minute).Add(-Minutes(10));

        var stop = RoundTimes.Calculate(departure, [Stop(10)], Minutes(10), windowMinutes: 60).Stops[0];

        Assert.Equal(new ExpectedWindow(new TimeOnly(fromHour, fromMinute), new TimeOnly(toHour, toMinute)), stop.Window);
    }

    [Fact]
    public void The_width_of_the_window_comes_from_the_settings()
    {
        var stop = RoundTimes.Calculate(new TimeOnly(9, 0), [Stop(60)], Minutes(10), windowMinutes: 120).Stops[0];

        Assert.Equal(new ExpectedWindow(new TimeOnly(9, 0), new TimeOnly(11, 0)), stop.Window);
    }

    [Fact]
    public void Arriving_before_a_fixed_time_means_waiting_until_then()
    {
        var times = RoundTimes.Calculate(new TimeOnly(8, 0), [Stop(20, fixedTime: new TimeOnly(10, 0)), Stop(15)], Minutes(25), 60);

        Assert.Equal(new TimeOnly(10, 0), times.Stops[0].Arrival);
        Assert.False(times.Stops[0].Late);
        Assert.Equal(new TimeOnly(10, 45), times.Stops[1].Arrival);
    }

    [Fact]
    public void Arriving_after_a_fixed_time_is_a_warning_and_the_real_arrival_counts()
    {
        var times = RoundTimes.Calculate(new TimeOnly(8, 0), [Stop(20), Stop(40, fixedTime: new TimeOnly(9, 0))], Minutes(25), 60);

        Assert.Equal(new TimeOnly(9, 30), times.Stops[1].Arrival);
        Assert.True(times.Stops[1].Late);
        Assert.False(times.Stops[0].Late);
    }

    [Fact]
    public void An_empty_round_comes_straight_back()
    {
        var times = RoundTimes.Calculate(new TimeOnly(18, 0), [], Minutes(0), 60);

        Assert.Empty(times.Stops);
        Assert.Equal(new TimeOnly(18, 0), times.Return);
    }

    [Fact]
    public void A_window_is_written_the_belgian_way()
    {
        Assert.Equal("9u30 – 10u30", new ExpectedWindow(new TimeOnly(9, 30), new TimeOnly(10, 30)).ToString());
    }

    [Theory]
    [InlineData("nl", "9u30 – 10u30")]
    [InlineData("fr", "9h30 – 10h30")]
    [InlineData("en", "9:30 – 10:30")]
    [InlineData("de", "9u30 – 10u30")]
    public void A_window_for_a_customer_is_written_the_way_of_their_language(string language, string expected)
    {
        Assert.Equal(expected, new ExpectedWindow(new TimeOnly(9, 30), new TimeOnly(10, 30)).ToString(language));
    }
}
