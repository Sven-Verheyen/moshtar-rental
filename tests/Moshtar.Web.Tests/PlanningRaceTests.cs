using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Maps;
using Moshtar.Application.Reservations;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Maps;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Tests;

/// <summary>
/// Wat in de Planning tegelijk gebeurt terwijl de kaartdienst nog rekent: een tweede knop in hetzelfde scherm, of een
/// adres dat intussen verandert. De kaartdienst is de lokale vervanger, die op commando wacht (<see cref="HeldMaps"/>).
/// </summary>
public sealed class PlanningRaceTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Business = new() { Street = "Grote Markt 1", PostalCode = "7000", City = "Bergen" };

    [Fact]
    public async Task A_second_round_action_in_the_same_screen_while_a_route_is_being_calculated_breaks_neither()
    {
        var day = TestReservations.FutureDay(250);
        var other = TestReservations.FutureDay(251);
        await MoveBusinessAsync(Business);
        await SquareAsync(day, "tegelijk");
        var (gent, _, _) = await SquareAsync(other, "ander");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IRoundPlanner>().ProposeAsync(other, RoundKind.Delivery);
        var maps = new HeldMaps { HoldRoutes = true };
        await using var held = WithMaps(maps);
        // Eén scope: één Blazor-circuit, zoals twee knoppen in hetzelfde scherm.
        await using var screen = await TenantScopeAsync(held);

        var proposing = screen.ServiceProvider.GetRequiredService<IRoundPlanner>().ProposeAsync(day, RoundKind.Delivery);
        await maps.Held;
        var whatsApp = screen.ServiceProvider.GetRequiredService<IRoundMessages>().RecordWhatsAppAsync(other, RoundKind.Delivery, gent.Id);
        await Task.Delay(200);
        maps.Release();
        await Task.WhenAll(proposing, whatsApp);

        var schedule = await ScheduleAsync(day);
        Assert.Equal(3, schedule.Deliveries.Count(s => s.InRound is not null));
        Assert.NotNull((await ScheduleAsync(other)).Deliveries.Single(s => s.ReservationId == gent.Id).InRound!.WhatsAppAt);
    }

    [Fact]
    public async Task A_business_address_changed_while_the_old_one_is_looked_up_keeps_no_old_point()
    {
        var day = TestReservations.FutureDay(252);
        var old = new Address { Street = "Oude Baan 1", PostalCode = "2400", City = "Mol" };
        var moved = new Address { Street = "Nieuwe Baan 3", PostalCode = "3000", City = "Leuven" };
        await MoveBusinessAsync(old);
        var maps = new HeldMaps { HoldFind = a => a.Street == old.Street };
        await using var held = WithMaps(maps);
        await using var screen = await TenantScopeAsync(held);

        var opening = screen.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
        await maps.Held;
        await MoveBusinessAsync(moved);
        maps.Release();
        await opening;

        Assert.Equal(await new LocalMapService().FindAsync(moved), (await ScheduleAsync(day)).BusinessPoint);
    }

    [Fact]
    public async Task A_delivery_address_corrected_while_the_old_one_is_looked_up_keeps_no_old_point()
    {
        var day = TestReservations.FutureDay(253);
        await MoveBusinessAsync(Business);
        var reservation = await ReserveAsync("Oscar", "Ouder", day, "Grote Markt 7", "2300", "Turnhout");
        // Zoals een reservatie van voor er punten waren.
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Reservations.SingleAsync(r => r.Id == reservation.Id)).DeliveryPoint = null;
            await db.SaveChangesAsync();
        }
        var maps = new HeldMaps { HoldFind = a => a.Street == "Grote Markt 7" };
        await using var held = WithMaps(maps);
        await using var screen = await TenantScopeAsync(held);

        var opening = screen.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
        await maps.Held;
        // Het verbeterde adres wordt (nog) niet gevonden: het blijft zonder punt.
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().CorrectDeliveryAddressAsync(
                reservation.Id, new Address { Street = "Kerkplein", PostalCode = "2300", City = "Turnhout" }, ReservationActor.Website);
        maps.Release();
        await opening;

        var stop = (await ScheduleAsync(day)).Deliveries.Single(s => s.ReservationId == reservation.Id);
        Assert.Equal("Kerkplein", stop.Address!.Street);
        Assert.Null(stop.Point);
    }

    /// <summary>Een variant van de app met deze kaartdienst, op dezelfde database.</summary>
    private WebApplicationFactory<Program> WithMaps(IMapService maps) =>
        app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(maps)));

    private static async Task<AsyncServiceScope> TenantScopeAsync(WebApplicationFactory<Program> factory)
    {
        var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == "hopsakee"));
        return scope;
    }

    private async Task<ScheduleDay> ScheduleAsync(DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
    }

    /// <summary>Een levering in Gent, Antwerpen en Namen op deze dag.</summary>
    private async Task<(Reservation Gent, Reservation Antwerpen, Reservation Namen)> SquareAsync(DateOnly day, string tag) => (
        await ReserveAsync("Greet", $"gent-{tag}", day, "Veldstraat 10", "9000", "Gent"),
        await ReserveAsync("Anton", $"antwerpen-{tag}", day, "Meir 10", "2000", "Antwerpen"),
        await ReserveAsync("Nico", $"namen-{tag}", day, "Rue de Fer 10", "5000", "Namen"));

    /// <summary>Bewaart een nieuw bedrijfsadres in Bedrijfsgegevens.</summary>
    private async Task MoveBusinessAsync(Address address)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
        await details.SaveAsync(await details.GetAsync() with { Street = address.Street, PostalCode = address.PostalCode, City = address.City });
    }

    private async Task<Reservation> ReserveAsync(string firstName, string lastName, DateOnly day, string street, string postalCode, string city)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == "springkasteel");
        if (item is null)
        {
            item = new RentalItem { Slug = "springkasteel", Stock = 50, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = "Springkasteel" }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        var address = new Address { Street = street, PostalCode = postalCode, City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day.AddDays(1)),
            [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    /// <summary>
    /// De lokale kaartdienst, maar een route (<see cref="HoldRoutes"/>) of het opzoeken van bepaalde adressen
    /// (<see cref="HoldFind"/>) wacht tot <see cref="Release"/>. <see cref="Held"/> is klaar zodra er een oproep wacht.
    /// </summary>
    private sealed class HeldMaps : IMapService
    {
        private readonly LocalMapService _maps = new();
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HoldRoutes { get; init; }
        public Func<Address, bool> HoldFind { get; init; } = _ => false;
        public Task Held => _held.Task;
        public void Release() => _released.TrySetResult();

        public async Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default)
        {
            if (HoldFind(address)) await HoldAsync();
            return await _maps.FindAsync(address, ct);
        }

        public async Task<Route> RouteAsync(GeoPoint start, IReadOnlyList<GeoPoint> stops, DateTimeOffset departure, bool bestOrder, CancellationToken ct = default)
        {
            if (HoldRoutes) await HoldAsync();
            return await _maps.RouteAsync(start, stops, departure, bestOrder, ct);
        }

        public Task<TimeSpan[][]> TravelTimesAsync(IReadOnlyList<GeoPoint> origins, IReadOnlyList<GeoPoint> destinations, DateTimeOffset departure, CancellationToken ct = default) =>
            _maps.TravelTimesAsync(origins, destinations, departure, ct);

        private Task HoldAsync()
        {
            _held.TrySetResult();
            return _released.Task;
        }
    }
}
