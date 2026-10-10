using Moshtar.Domain.Reservations;

namespace Moshtar.Domain.Tests;

public class ReservationStatusTests
{
    [Theory]
    [InlineData(ReservationStatus.Delivered)]
    [InlineData(ReservationStatus.Returned)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    public void Confirmed_reservation_can_move_to_another_status(ReservationStatus status)
    {
        var reservation = new Reservation();

        reservation.ChangeStatus(status, ReservationActor.Website, DateTime.UtcNow);

        Assert.Equal(status, reservation.Status);
    }

    [Fact]
    public void Cancelled_reservation_cannot_be_revived_because_its_stock_may_be_gone()
    {
        var reservation = new Reservation();
        reservation.ChangeStatus(ReservationStatus.Cancelled, ReservationActor.Website, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => reservation.ChangeStatus(ReservationStatus.Confirmed, ReservationActor.Website, DateTime.UtcNow));
    }
}
