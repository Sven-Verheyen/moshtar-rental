using Moshtar.Domain.Carts;
using Moshtar.Domain.Common;

namespace Moshtar.Domain.Tests;

public class CartTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateRange June = new(new DateOnly(2027, 6, 12), new DateOnly(2027, 6, 13));
    private static readonly CatalogRef Castle = CatalogRef.Item(Guid.NewGuid());
    private static readonly CatalogRef Party = CatalogRef.Bundle(Guid.NewGuid());

    [Fact]
    public void The_same_item_twice_is_one_line_with_more_pieces()
    {
        var cart = Cart.Create("sleutel", June, Now);

        cart.Add(Castle, 1, June, Now);
        cart.Add(Castle, 2, June, Now);
        cart.Add(Party, 1, June, Now);

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(3, cart.Lines.Single(l => l.Entry == Castle).Quantity);
        Assert.Equal(4, cart.Count);
    }

    [Fact]
    public void Adding_with_another_period_moves_the_whole_cart()
    {
        var cart = Cart.Create("sleutel", June, Now);
        var july = new DateRange(new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 1));

        cart.Add(Castle, 1, June, Now);
        cart.Add(Party, 1, july, Now.AddHours(1));

        Assert.Equal(july, cart.Period);
        Assert.Equal(Now.AddHours(1), cart.UpdatedAtUtc);
    }

    [Fact]
    public void Zero_pieces_removes_the_line_and_more_than_the_maximum_is_capped()
    {
        var cart = Cart.Create("sleutel", June, Now);
        cart.Add(Castle, 1, June, Now);
        cart.Add(Party, 1, June, Now);

        cart.SetQuantity(Castle, 0, Now);
        cart.SetQuantity(Party, 99, Now);

        Assert.Equal(Party, Assert.Single(cart.Lines).Entry);
        Assert.Equal(Cart.MaxQuantity, cart.Count);
    }

    [Fact]
    public void Another_period_keeps_everything_in_the_cart()
    {
        var cart = Cart.Create("sleutel", June, Now);
        cart.Add(Castle, 2, June, Now);
        var august = new DateRange(new DateOnly(2027, 8, 1), new DateOnly(2027, 8, 3));

        cart.ChangePeriod(august, Now.AddDays(1));

        Assert.Equal((august, 2), (cart.Period, cart.Count));
        Assert.Equal(Now.AddDays(1), cart.UpdatedAtUtc);
    }
}
