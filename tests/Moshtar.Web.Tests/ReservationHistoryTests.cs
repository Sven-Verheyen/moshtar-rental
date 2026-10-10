using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Web.Identity;

namespace Moshtar.Web.Tests;

/// <summary>Elke reservatie houdt bij wie ze aanmaakte, van status veranderde of annuleerde, en wanneer.</summary>
public sealed class ReservationHistoryTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Reservation_on_the_website_is_created_via_website()
    {
        var before = DateTime.UtcNow;
        var id = await TestReservations.ReserveAsync(app, "via-website", TestReservations.FutureDay(218));

        var history = await HistoryAsync("hopsakee", id);

        var created = Assert.Single(history);
        Assert.Equal(ReservationEventKind.Created, created.Kind);
        Assert.Null(created.UserEmail);
        Assert.InRange(created.OccurredAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task Status_change_and_cancellation_are_kept_with_the_user_newest_first()
    {
        var anna = await app.CreateUserAsync("hopsakee", "anna@hopsakee.test", UserRole.Staff);
        var ben = await app.CreateUserAsync("hopsakee", "ben@hopsakee.test", UserRole.Staff);
        var id = await TestReservations.ReserveAsync(app, "gewijzigd", TestReservations.FutureDay(219));

        await ChangeStatusAsync(id, ReservationStatus.Delivered, ReservationActor.User(anna.Id));
        await ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.User(ben.Id));
        var history = await HistoryAsync("hopsakee", id);

        Assert.Equal(
            [
                (ReservationEventKind.Cancelled, ReservationStatus.Cancelled, "ben@hopsakee.test"),
                (ReservationEventKind.StatusChanged, ReservationStatus.Delivered, "anna@hopsakee.test"),
                (ReservationEventKind.Created, ReservationStatus.Confirmed, (string?)null),
            ],
            history.Select(e => (e.Kind, e.Status, e.UserEmail)));
        Assert.True(history[0].OccurredAtUtc >= history[1].OccurredAtUtc);
    }

    [Fact]
    public async Task A_refused_change_leaves_no_trace_in_the_history()
    {
        var user = await app.CreateUserAsync("hopsakee", "geweigerd@hopsakee.test", UserRole.Staff);
        var id = await TestReservations.ReserveAsync(app, "geweigerd", TestReservations.FutureDay(220));
        await ChangeStatusAsync(id, ReservationStatus.Cancelled, ReservationActor.User(user.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ChangeStatusAsync(id, ReservationStatus.Confirmed, ReservationActor.User(user.Id)));

        Assert.Equal(2, (await HistoryAsync("hopsakee", id)).Count);
    }

    [Fact]
    public async Task A_disabled_user_stays_visible_in_the_history()
    {
        var user = await app.CreateUserAsync("hopsakee", "vertrokken@hopsakee.test", UserRole.Staff);
        var id = await TestReservations.ReserveAsync(app, "vertrokken", TestReservations.FutureDay(221));
        await ChangeStatusAsync(id, ReservationStatus.Delivered, ReservationActor.User(user.Id));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<UserAdministration>().DisableAsync(user.Id);

        var history = await HistoryAsync("hopsakee", id);

        Assert.Equal("vertrokken@hopsakee.test", history[0].UserEmail);
    }

    [Fact]
    public async Task Another_rental_company_does_not_see_the_history()
    {
        var id = await TestReservations.ReserveAsync(app, "andermans-historiek", TestReservations.FutureDay(222));

        Assert.Empty(await HistoryAsync("andere", id));
    }

    private async Task ChangeStatusAsync(Guid id, ReservationStatus status, ReservationActor actor)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(id, status, actor);
    }

    private async Task<IReadOnlyList<ReservationHistoryEntry>> HistoryAsync(string tenantSlug, Guid id)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(id);
    }
}
