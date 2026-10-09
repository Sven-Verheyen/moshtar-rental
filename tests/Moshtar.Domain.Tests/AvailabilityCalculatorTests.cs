using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Domain.Tests;

public class AvailabilityCalculatorTests
{
    private static readonly Guid Castle = Guid.NewGuid();
    private static readonly Guid Game = Guid.NewGuid();
    private static readonly Dictionary<Guid, int> Stock = new() { [Castle] = 1, [Game] = 3 };

    private static DateRange Days(int startDay, int endDay) =>
        new(new DateOnly(2026, 6, startDay), new DateOnly(2026, 6, endDay));

    [Fact]
    public void Free_item_is_available()
    {
        var shortages = AvailabilityCalculator.FindShortages(Stock, [], [], [new(Castle, 1, Days(6, 7))]);
        Assert.Empty(shortages);
    }

    [Fact]
    public void Last_unit_cannot_be_reserved_twice_on_overlapping_days()
    {
        var shortages = AvailabilityCalculator.FindShortages(
            Stock, reserved: [new(Castle, 1, Days(6, 7))], blocked: [], requested: [new(Castle, 1, Days(7, 8))]);

        var shortage = Assert.Single(shortages);
        Assert.Equal(new DateOnly(2026, 6, 7), shortage.Day);
        Assert.Equal(2, shortage.Required);
    }

    [Fact]
    public void Adjacent_rentals_fit_without_buffer()
    {
        var shortages = AvailabilityCalculator.FindShortages(
            Stock, reserved: [new(Castle, 1, Days(6, 7))], blocked: [], requested: [new(Castle, 1, Days(8, 8))]);
        Assert.Empty(shortages);
    }

    [Fact]
    public void Buffer_day_after_rental_blocks_the_next_day()
    {
        var shortages = AvailabilityCalculator.FindShortages(
            Stock, reserved: [new(Castle, 1, Days(6, 7))], blocked: [], requested: [new(Castle, 1, Days(8, 8))],
            bufferDaysAfter: 1);
        Assert.NotEmpty(shortages);
    }

    [Fact]
    public void Multiple_units_can_be_rented_up_to_stock()
    {
        var reserved = new[] { new StockDemand(Game, 2, Days(6, 6)) };
        Assert.Empty(AvailabilityCalculator.FindShortages(Stock, reserved, [], [new(Game, 1, Days(6, 6))]));
        Assert.NotEmpty(AvailabilityCalculator.FindShortages(Stock, reserved, [], [new(Game, 2, Days(6, 6))]));
    }

    [Fact]
    public void Blockout_reduces_available_stock()
    {
        var shortages = AvailabilityCalculator.FindShortages(
            Stock, reserved: [], blocked: [new(Castle, 1, Days(1, 10))], requested: [new(Castle, 1, Days(6, 6))]);
        Assert.NotEmpty(shortages);
    }

    [Fact]
    public void Unknown_item_has_no_stock()
    {
        var shortages = AvailabilityCalculator.FindShortages(Stock, [], [], [new(Guid.NewGuid(), 1, Days(6, 6))]);
        Assert.NotEmpty(shortages);
    }

    [Fact]
    public void Bundle_occupies_its_items()
    {
        var bundle = new Bundle { Items = [new BundleItem { RentalItemId = Castle, Quantity = 1 }, new BundleItem { RentalItemId = Game, Quantity = 2 }] };
        var bundles = new Dictionary<Guid, Bundle> { [bundle.Id] = bundle };
        var reservedLines = new[] { new ReservationLine { BundleId = bundle.Id, Quantity = 1 } };
        var reserved = DemandExpander.Expand(reservedLines, Days(6, 7), bundles).ToList();

        // Het springkasteel zit in het pakket, dus los reserveren lukt niet meer...
        Assert.NotEmpty(AvailabilityCalculator.FindShortages(Stock, reserved, [], [new(Castle, 1, Days(7, 7))]));
        // ...maar van de spellen is er nog één over.
        Assert.Empty(AvailabilityCalculator.FindShortages(Stock, reserved, [], [new(Game, 1, Days(7, 7))]));
        Assert.NotEmpty(AvailabilityCalculator.FindShortages(Stock, reserved, [], [new(Game, 2, Days(7, 7))]));
    }
}
