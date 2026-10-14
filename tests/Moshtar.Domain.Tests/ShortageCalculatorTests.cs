using Moshtar.Domain.Availability;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Tests;

/// <summary>
/// Een tekort: een dag, vanaf vandaag, waarop van een artikel meer exemplaren gereserveerd zijn dan er verhuurbaar
/// zijn, met de reservaties die het artikel op die dag gebruiken.
/// </summary>
public class ShortageCalculatorTests
{
    private static readonly Guid Castle = Guid.NewGuid();
    private static readonly Guid Game = Guid.NewGuid();
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static DateOnly June(int day) => new(2026, 6, day);
    private static DateRange Days(int startDay, int endDay) => new(June(startDay), June(endDay));

    [Fact]
    public void Reservations_within_the_stock_give_no_shortage()
    {
        var shortages = ShortageCalculator.Find(
            new Dictionary<Guid, int> { [Castle] = 2 },
            [new(First, new(Castle, 1, Days(6, 7))), new(Second, new(Castle, 1, Days(7, 8)))],
            [], Today);

        Assert.Empty(shortages);
    }

    [Fact]
    public void A_lower_stock_gives_a_shortage_on_each_day_with_too_many_reserved()
    {
        var shortages = ShortageCalculator.Find(
            new Dictionary<Guid, int> { [Castle] = 1, [Game] = 5 },
            [new(First, new(Castle, 1, Days(6, 7))), new(Second, new(Castle, 1, Days(7, 8))), new(Second, new(Game, 2, Days(7, 8)))],
            [], Today);

        var shortage = Assert.Single(shortages);
        Assert.Equal((Castle, June(7), 1, 2, 1), (shortage.RentalItemId, shortage.Day, shortage.Rentable, shortage.Reserved, shortage.Missing));
        Assert.Equal([First, Second], shortage.ReservationIds);
    }

    [Fact]
    public void A_blockout_lowers_what_is_rentable()
    {
        var shortages = ShortageCalculator.Find(
            new Dictionary<Guid, int> { [Game] = 3 },
            [new(First, new(Game, 2, Days(6, 8)))],
            [new(Game, 2, Days(8, 9))], Today);

        var shortage = Assert.Single(shortages);
        Assert.Equal((June(8), 1, 2), (shortage.Day, shortage.Rentable, shortage.Reserved));
    }

    [Fact]
    public void Days_before_today_do_not_count()
    {
        var shortages = ShortageCalculator.Find(
            new Dictionary<Guid, int> { [Castle] = 0 },
            [new(First, new(Castle, 1, new DateRange(Today.AddDays(-2), Today.AddDays(1))))],
            [], Today);

        Assert.Equal([Today, Today.AddDays(1)], shortages.Select(s => s.Day));
    }

    [Fact]
    public void Buffer_days_count_as_reserved()
    {
        // Met een bufferdag na elke verhuur raken twee verhuringen op opeenvolgende dagen elkaar.
        var shortages = ShortageCalculator.Find(
            new Dictionary<Guid, int> { [Castle] = 1 },
            [new(First, new(Castle, 1, Days(6, 6))), new(Second, new(Castle, 1, Days(7, 7)))],
            [], Today, bufferDaysAfter: 1);

        var shortage = Assert.Single(shortages);
        Assert.Equal((June(7), 2), (shortage.Day, shortage.Reserved));
        Assert.Equal([First, Second], shortage.ReservationIds);
    }

    [Fact]
    public void An_item_without_known_stock_has_nothing_rentable()
    {
        var shortage = Assert.Single(ShortageCalculator.Find(new Dictionary<Guid, int>(), [new(First, new(Castle, 1, Days(6, 6)))], [], Today));

        Assert.Equal((0, 1), (shortage.Rentable, shortage.Reserved));
    }
}
