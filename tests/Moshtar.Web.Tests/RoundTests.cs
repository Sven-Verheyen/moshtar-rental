using System.Net;
using System.Text.RegularExpressions;
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
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Maps;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Route en verwachte uren van een ronde, met de lokale vervanger van Azure Maps: die rijdt in vogelvlucht aan een
/// vaste snelheid (<see cref="LocalMapService.Travel"/>) en zoekt de snelste volgorde exact. Het bedrijf ligt in Bergen
/// (7000); Gent (9000), Antwerpen (2000) en Namen (5000) liggen er als een vierkant rond, dus de snelste ronde rijdt
/// het vierkant rond, met Antwerpen in het midden, in welke richting ook.
/// </summary>
public sealed class RoundTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Business = new() { Street = "Grote Markt 1", PostalCode = "7000", City = "Bergen" };

    [Fact]
    public async Task Proposing_a_route_saves_the_fastest_order_from_and_back_to_the_business_with_arrival_and_window()
    {
        var day = TestReservations.FutureDay(200);
        var (gent, antwerpen, namen) = await SquareAsync(day);
        var client = await app.LoggedInClientAsync("ronde-voorstellen@hopsakee.test", UserRole.Staff);
        var before = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("Route voorstellen", before);
        Assert.DoesNotContain("data-order=", before);

        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var round = Section(await PlanningAsync(client, day), "leverronde");

        var order = Order(round);
        Assert.Equal(antwerpen.Number, order[1]);
        Assert.Equal([gent.Number, namen.Number], order.Where(n => n != antwerpen.Number).Order());
        var first = order[0] == gent.Number ? gent : namen;
        var firstArrival = new TimeOnly(8, 0).Add(LocalMapService.Travel(await PointAsync(Business), first.DeliveryPoint!));
        Assert.Equal(firstArrival, Arrival(round, first.Number));
        Assert.Equal(firstArrival.AddMinutes(30).Add(LocalMapService.Travel(first.DeliveryPoint!, antwerpen.DeliveryPoint!)), Arrival(round, antwerpen.Number));
        Assert.Contains($"data-window=\"{RoundTimes.Window(firstArrival, 60)}\"", Stop(round, first.Number));
        Assert.Contains("data-departure=\"08:00\"", round);
    }

    [Fact]
    public async Task Moving_a_stop_keeps_the_new_order_and_recalculates_the_times()
    {
        var day = TestReservations.FutureDay(201);
        await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-verslepen@hopsakee.test", UserRole.Staff);
        var proposed = Order(Section(await PlanningAsync(client, day), "leverronde"));

        await PlanAsync(async p => await p.MoveAsync(day, RoundKind.Delivery, await IdAsync(proposed[2]), 1));
        var round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.Equal([proposed[2], proposed[0], proposed[1]], Order(round));
        var first = await ReservationAsync(proposed[2]);
        Assert.Equal(new TimeOnly(8, 0).Add(LocalMapService.Travel(await PointAsync(Business), first.DeliveryPoint!)), Arrival(round, proposed[2]));
    }

    [Fact]
    public async Task Departure_own_setup_time_and_a_fixed_time_recalculate_the_times_and_are_kept()
    {
        var day = TestReservations.FutureDay(202);
        await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-uren@hopsakee.test", UserRole.Staff);
        var round = Section(await PlanningAsync(client, day), "leverronde");
        var order = Order(round);
        var (first, second, third) = (Arrival(round, order[0]), Arrival(round, order[1]), Arrival(round, order[2]));

        await PlanAsync(p => p.SetDepartureAsync(day, RoundKind.Delivery, new TimeOnly(7, 0)));
        round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("data-departure=\"07:00\"", round);
        Assert.Equal(first.AddHours(-1), Arrival(round, order[0]));

        await PlanAsync(async p => await p.SetHandlingMinutesAsync(day, RoundKind.Delivery, await IdAsync(order[0]), 90));
        round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Equal(second, Arrival(round, order[1]));

        var waitUntil = new TimeOnly(13, 0);
        await PlanAsync(async p => await p.SetFixedTimeAsync(day, RoundKind.Delivery, await IdAsync(order[1]), waitUntil));
        round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Equal(waitUntil, Arrival(round, order[1]));
        Assert.Equal(waitUntil.Add(third - second), Arrival(round, order[2]));
        Assert.DoesNotContain("Te laat", Stop(round, order[1]));

        await PlanAsync(async p => await p.SetFixedTimeAsync(day, RoundKind.Delivery, await IdAsync(order[2]), new TimeOnly(6, 0)));
        round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("Te laat", Stop(round, order[2]));
        Assert.Contains("data-fixed=\"06:00\"", Stop(round, order[2]));
    }

    [Fact]
    public async Task A_new_reservation_after_the_route_shows_round_changed_and_goes_where_it_adds_the_least_travel()
    {
        var day = TestReservations.FutureDay(203);
        var (_, antwerpen, namen) = await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-nieuw@hopsakee.test", UserRole.Staff);
        Assert.DoesNotContain("Ronde gewijzigd", Section(await PlanningAsync(client, day), "leverronde"));

        // Brussel ligt binnen het vierkant, het dichtst bij de lijn van Antwerpen naar Namen.
        var brussel = await ReserveAsync("Bert", "Brussel", day, day, "Wetstraat 10", "1000", "Brussel");
        var lost = await ReserveAsync("Nina", "Nergens", day, day, "Ergens in het bos", "1000", "Brussel");
        var round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("Ronde gewijzigd", round);

        await PlanAsync(p => p.UpdateAsync(day, RoundKind.Delivery));
        round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.DoesNotContain("Ronde gewijzigd", round);
        var order = Order(round);
        Assert.Equal(4, order.Count);
        var position = order.IndexOf(brussel.Number);
        Assert.InRange(position, 1, 2);
        Assert.Equal(new[] { antwerpen.Number, namen.Number }.Order(), new[] { order[position - 1], order[position + 1] }.Order());
        Assert.Contains("Adres niet gevonden", Stop(round, lost.Number));
        Assert.DoesNotContain("data-order=", Stop(round, lost.Number));
    }

    [Fact]
    public async Task A_cancelled_reservation_shows_round_changed_and_drops_out_of_the_round()
    {
        var day = TestReservations.FutureDay(204);
        var (gent, antwerpen, namen) = await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-weg@hopsakee.test", UserRole.Staff);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(gent.Id, ReservationStatus.Cancelled, ReservationActor.Website);
        Assert.Contains("Ronde gewijzigd", Section(await PlanningAsync(client, day), "leverronde"));

        await PlanAsync(p => p.UpdateAsync(day, RoundKind.Delivery));
        var round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.DoesNotContain("Ronde gewijzigd", round);
        Assert.Equal([antwerpen.Number, namen.Number], Order(round).Order());
        var first = Order(round)[0] == antwerpen.Number ? antwerpen : namen;
        Assert.Equal(new TimeOnly(8, 0).Add(LocalMapService.Travel(await PointAsync(Business), first.DeliveryPoint!)), Arrival(round, first.Number));
    }

    [Fact]
    public async Task The_collection_round_uses_the_departure_and_teardown_time_from_the_business_details()
    {
        var day = TestReservations.FutureDay(210);
        await MoveBusinessAsync(Business);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
            var current = await details.GetAsync();
            await details.SaveAsync(current with { Rounds = current.Rounds with { CollectionDeparture = new TimeOnly(17, 0), TeardownMinutes = 25 } });
        }
        var gent = await ReserveAsync("Greet", "Gent", day.AddDays(-1), day, "Veldstraat 10", "9000", "Gent");
        var namen = await ReserveAsync("Nico", "Namen", day.AddDays(-1), day, "Rue de Fer 10", "5000", "Namen");
        var client = await app.LoggedInClientAsync("ronde-ophaling@hopsakee.test", UserRole.Staff);

        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Collection));
        var round = Section(await PlanningAsync(client, day), "ophaalronde");

        Assert.Contains("data-departure=\"17:00\"", round);
        var order = Order(round);
        var (first, second) = order[0] == gent.Number ? (gent, namen) : (namen, gent);
        var arrival = new TimeOnly(17, 0).Add(LocalMapService.Travel(await PointAsync(Business), first.DeliveryPoint!));
        Assert.Equal(arrival, Arrival(round, first.Number));
        Assert.Equal(arrival.AddMinutes(25).Add(LocalMapService.Travel(first.DeliveryPoint!, second.DeliveryPoint!)), Arrival(round, second.Number));
        Assert.DoesNotContain("data-order=", Section(await PlanningAsync(client, day), "leverronde"));
    }

    [Fact]
    public async Task The_map_numbers_the_stops_in_the_order_of_the_route()
    {
        var day = TestReservations.FutureDay(215);
        var (gent, antwerpen, namen) = await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        await using var withMap = app.WithWebHostBuilder(b => b.UseSetting("Maps:AzureMapsBrowserKey", "test-sleutel"));
        await app.CreateUserAsync("hopsakee", "ronde-kaart@hopsakee.test", UserRole.Staff);
        var client = withMap.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{MoshtarApp.HopsakeeHost}"), AllowAutoRedirect = false,
        });
        await BackOffice.LoginAsync(client, "ronde-kaart@hopsakee.test", MoshtarApp.Password);

        var html = await PlanningAsync(client, day);

        var start = html.IndexOf("id=\"planning-kaart\"", StringComparison.Ordinal);
        var map = html[start..html.IndexOf('>', start)];
        Assert.Contains($"\"nummer\":\"{antwerpen.Number}\",\"klant\":\"Anton Antwerpen\",\"ronde\":\"levering\",\"punt\":\"{antwerpen.DeliveryPoint}\",\"volgorde\":2", map);
        Assert.Contains($"\"nummer\":\"{gent.Number}\"", map);
        Assert.Contains($"\"nummer\":\"{namen.Number}\"", map);
    }

    [Fact]
    public async Task A_round_that_runs_past_midnight_shows_a_warning()
    {
        var day = TestReservations.FutureDay(205);
        await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-middernacht@hopsakee.test", UserRole.Staff);
        Assert.DoesNotContain("loopt over middernacht", Section(await PlanningAsync(client, day), "leverronde"));

        await PlanAsync(p => p.SetDepartureAsync(day, RoundKind.Delivery, new TimeOnly(23, 0)));

        Assert.Contains("Deze ronde loopt over middernacht", Section(await PlanningAsync(client, day), "leverronde"));
    }

    [Fact]
    public async Task An_improved_address_keeps_the_place_and_settings_of_the_stop_and_recalculates_its_times()
    {
        var day = TestReservations.FutureDay(206);
        await SquareAsync(day);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("ronde-adres@hopsakee.test", UserRole.Staff);
        var order = Order(Section(await PlanningAsync(client, day), "leverronde"));
        var first = await ReservationAsync(order[0]);
        await PlanAsync(p => p.SetFixedTimeAsync(day, RoundKind.Delivery, first.Id, new TimeOnly(6, 0)));
        await PlanAsync(p => p.SetHandlingMinutesAsync(day, RoundKind.Delivery, first.Id, 45));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IRoundMessages>().MailExpectedTimesAsync(day, RoundKind.Delivery, ReservationActor.Website);

        var moved = new Address { Street = "Andere straat 77", PostalCode = first.DeliveryAddress!.PostalCode, City = first.DeliveryAddress.City };
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().CorrectDeliveryAddressAsync(first.Id, moved, ReservationActor.Website);
        Assert.Contains("Ronde gewijzigd", Section(await PlanningAsync(client, day), "leverronde"));
        await PlanAsync(p => p.UpdateAsync(day, RoundKind.Delivery));
        var round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.DoesNotContain("Ronde gewijzigd", round);
        Assert.Equal(order, Order(round));
        var stop = Stop(round, first.Number);
        Assert.Contains("data-fixed=\"06:00\"", stop);
        Assert.Contains("Opbouwtijd 45 min", stop);
        Assert.Contains("data-mailed-window=", stop);
        Assert.Equal(new TimeOnly(8, 0).Add(LocalMapService.Travel(await PointAsync(Business), (await PointAsync(moved)))), Arrival(round, first.Number));
    }

    [Fact]
    public async Task Proposing_a_route_several_times_at_once_makes_one_round()
    {
        var day = TestReservations.FutureDay(207);
        await SquareAsync(day);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery)))));

        var client = await app.LoggedInClientAsync("ronde-tegelijk@hopsakee.test", UserRole.Staff);
        var round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Equal(3, Order(round).Count);
        Assert.DoesNotContain("Ronde gewijzigd", round);
    }

    /// <summary>Het bedrijf in Bergen, en een levering in Gent, Antwerpen en Namen op deze dag.</summary>
    private async Task<(Reservation Gent, Reservation Antwerpen, Reservation Namen)> SquareAsync(DateOnly day)
    {
        await MoveBusinessAsync(Business);
        return (
            await ReserveAsync("Greet", "Gent", day, day.AddDays(1), "Veldstraat 10", "9000", "Gent"),
            await ReserveAsync("Anton", "Antwerpen", day, day.AddDays(1), "Meir 10", "2000", "Antwerpen"),
            await ReserveAsync("Nico", "Namen", day, day.AddDays(1), "Rue de Fer 10", "5000", "Namen"));
    }

    private async Task PlanAsync(Func<IRoundPlanner, Task> plan)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await plan(scope.ServiceProvider.GetRequiredService<IRoundPlanner>());
    }

    private async Task<GeoPoint> PointAsync(Address address) =>
        (await app.Services.GetRequiredService<IMapService>().FindAsync(address))!;

    private async Task<Guid> IdAsync(string number) => (await ReservationAsync(number)).Id;

    private async Task<Reservation> ReservationAsync(string number)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync(r => r.Number == number);
    }

    /// <summary>Bewaart een nieuw bedrijfsadres in Bedrijfsgegevens.</summary>
    private async Task MoveBusinessAsync(Address address)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
        await details.SaveAsync(await details.GetAsync() with { Street = address.Street, PostalCode = address.PostalCode, City = address.City });
    }

    /// <summary>De reservatienummers van de genummerde stops, in de volgorde van hun nummer.</summary>
    private static List<string> Order(string section) =>
        Regex.Matches(section, "data-reservation=\"([^\"]+)\"[^>]*data-order=\"(\\d+)\"")
            .Select(m => (Number: m.Groups[1].Value, Order: int.Parse(m.Groups[2].Value)))
            .OrderBy(s => s.Order)
            .Select(s => s.Number)
            .ToList();

    private static TimeOnly Arrival(string section, string number)
    {
        var match = Regex.Match(Stop(section, number), "data-arrival=\"(\\d\\d:\\d\\d)\"");
        Assert.True(match.Success, $"Geen aankomstuur bij {number}.");
        return TimeOnly.ParseExact(match.Groups[1].Value, "HH:mm");
    }

    private static async Task<string> PlanningAsync(HttpClient client, DateOnly day)
    {
        var response = await client.GetAsync($"/admin/planning?dag={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Het stuk van de pagina tussen het begin van de sectie met deze id en de volgende sectie.</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Sectie {id} niet gevonden.");
        var end = html.IndexOf("<section", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    /// <summary>Het kaartje van één reservatie binnen een sectie, tot het volgende kaartje.</summary>
    private static string Stop(string section, string number)
    {
        var start = section.IndexOf($"data-reservation=\"{number}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Reservatie {number} niet gevonden.");
        var end = section.IndexOf("data-reservation=", start + 1, StringComparison.Ordinal);
        return end < 0 ? section[start..] : section[start..end];
    }

    private async Task<Reservation> ReserveAsync(
        string firstName, string lastName, DateOnly start, DateOnly end, string street, string postalCode, string city)
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
            new DateRange(start, end),
            [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }
}
