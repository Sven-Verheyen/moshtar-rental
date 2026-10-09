using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(1, 150)]
    [InlineData(2, 225)]
    [InlineData(3, 300)]
    public void First_day_full_price_extra_days_reduced(int days, decimal expected) =>
        Assert.Equal(expected, new Pricing { DayPrice = 150, ExtraDayPrice = 75 }.PriceFor(days));

    [Fact]
    public void Without_extra_day_price_every_day_costs_the_day_price() =>
        Assert.Equal(300, new Pricing { DayPrice = 100 }.PriceFor(3));

    [Fact]
    public void Date_range_counts_both_ends() =>
        Assert.Equal(2, new DateRange(new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 7)).Days);

    [Fact]
    public void Date_range_rejects_end_before_start() =>
        Assert.Throws<ArgumentException>(() => new DateRange(new DateOnly(2026, 6, 7), new DateOnly(2026, 6, 6)));
}
