using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Moshtar.Application.Maps;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;

namespace Moshtar.Web.Tests;

/// <summary>
/// Adressen op de kaart in de Planning, met de lokale vervanger van Azure Maps: die vindt een adres met een
/// Belgische postcode en een huisnummer, en geen adres zonder huisnummer.
/// </summary>
public sealed class DeliveryPointTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task A_stop_whose_address_is_not_found_gets_a_warning()
    {
        var day = TestReservations.FutureDay(330);
        var lost = await ReserveAsync("Nina", "Nergens", day, day.AddDays(1), "Ergens in het bos", "2400", "Mol");
        var found = await ReserveAsync("Gust", "Gevonden", day, day.AddDays(1), "Markt 1", "2400", "Mol");
        var client = await app.LoggedInClientAsync("kaart-waarschuwing@hopsakee.test", UserRole.Staff);

        var round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.Contains("Adres niet gevonden", Stop(round, lost.Number));
        Assert.DoesNotContain("Adres niet gevonden", Stop(round, found.Number));
    }

    [Fact]
    public async Task Reserving_still_works_when_the_map_service_does_not_answer()
    {
        var day = TestReservations.FutureDay(331);
        await using var broken = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IMapService, UnreachableMaps>()));
        await using var scope = broken.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == "hopsakee"));
        var address = new Address { Street = "Markt 2", PostalCode = "2400", City = "Mol" };

        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day), [new ReservationLineRequest(await ItemAsync(scope), null, 1)],
            new ReservationCustomer("Stijn", "Storing", "stijn.storing@example.test", "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, null, "nl"), ReservationActor.Website);

        Assert.True(result.Succeeded);
        Assert.Null(result.Reservation!.DeliveryPoint);
    }

    [Fact]
    public async Task An_existing_reservation_without_a_point_gets_one_when_its_day_is_opened_and_navigate_uses_it()
    {
        var day = TestReservations.FutureDay(332);
        var earlier = await ReserveAsync("Oscar", "Ouder", day, day, "Grote Markt 7", "2300", "Turnhout");
        // Zoals een reservatie van voor er punten waren.
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Reservations.SingleAsync(r => r.Id == earlier.Id)).DeliveryPoint = null;
            await db.SaveChangesAsync();
        }
        var client = await app.LoggedInClientAsync("kaart-bestaand@hopsakee.test", UserRole.Staff);
        var expected = await app.Services.GetRequiredService<IMapService>().FindAsync(earlier.DeliveryAddress!);

        var stop = Stop(Section(await PlanningAsync(client, day), "leverronde"), earlier.Number);

        Assert.DoesNotContain("Adres niet gevonden", stop);
        Assert.Contains($"href=\"https://waze.com/ul?ll={expected}&navigate=yes\"", stop.Replace("&amp;", "&"));
        Assert.Equal(expected, (await ReservationAsync(earlier.Id)).DeliveryPoint);
    }

    [Fact]
    public async Task Correcting_the_delivery_address_finds_it_again_and_is_kept_in_the_history()
    {
        var day = TestReservations.FutureDay(333);
        var user = await app.CreateUserAsync("hopsakee", "kaart-verbeteraar@hopsakee.test", UserRole.Staff);
        var reservation = await ReserveAsync("Vera", "Verkeerd", day, day, "Kerkstraat", "2440", "Geel");
        var client = await app.LoggedInClientAsync("kaart-verbeteren@hopsakee.test", UserRole.Staff);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().CorrectDeliveryAddressAsync(
                reservation.Id, new Address { Street = "Kerkstraat 14", PostalCode = "2440", City = "Geel" }, ReservationActor.User(user.Id));
        var stop = Stop(Section(await PlanningAsync(client, day), "leverronde"), reservation.Number);

        Assert.Contains("Kerkstraat 14, 2440 Geel", stop);
        Assert.DoesNotContain("Adres niet gevonden", stop);
        var history = await HistoryAsync(reservation.Id);
        Assert.Equal((ReservationEventKind.AddressCorrected, "kaart-verbeteraar@hopsakee.test"), (history[0].Kind, history[0].UserEmail));
    }

    [Fact]
    public async Task An_address_that_is_still_not_found_after_correcting_keeps_its_warning()
    {
        var day = TestReservations.FutureDay(334);
        var reservation = await ReserveAsync("Wim", "Weernergens", day, day, "Kerkstraat", "2440", "Geel");
        var client = await app.LoggedInClientAsync("kaart-nogaltijd@hopsakee.test", UserRole.Staff);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().CorrectDeliveryAddressAsync(
                reservation.Id, new Address { Street = "Kerkplein", PostalCode = "2440", City = "Geel" }, ReservationActor.Website);
        var stop = Stop(Section(await PlanningAsync(client, day), "leverronde"), reservation.Number);

        Assert.Contains("Kerkplein, 2440 Geel", stop);
        Assert.Contains("Adres niet gevonden", stop);
    }

    [Fact]
    public async Task A_confirmed_pin_counts_as_found_and_is_kept_in_the_history()
    {
        var day = TestReservations.FutureDay(335);
        var user = await app.CreateUserAsync("hopsakee", "kaart-speld@hopsakee.test", UserRole.Administrator);
        var reservation = await ReserveAsync("Pieter", "Polder", day, day.AddDays(1), "Hoeve De Kroon", "2490", "Balen");
        var client = await app.LoggedInClientAsync("kaart-speld-kijker@hopsakee.test", UserRole.Staff);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ConfirmDeliveryPointAsync(
                reservation.Id, new GeoPoint(51.1702, 5.1703), ReservationActor.User(user.Id));
        var stop = Stop(Section(await PlanningAsync(client, day), "leverronde"), reservation.Number);

        Assert.DoesNotContain("Adres niet gevonden", stop);
        Assert.Contains("href=\"https://waze.com/ul?ll=51.1702,5.1703&navigate=yes\"", stop.Replace("&amp;", "&"));
        Assert.Contains("Speld bevestigd", stop);
        var history = await HistoryAsync(reservation.Id);
        Assert.Equal((ReservationEventKind.PointConfirmed, "kaart-speld@hopsakee.test"), (history[0].Kind, history[0].UserEmail));
    }

    [Fact]
    public async Task A_pin_off_the_map_is_refused()
    {
        var reservation = await ReserveAsync("Bert", "Buitenaards", TestReservations.FutureDay(336), TestReservations.FutureDay(336), "Nergens", "2490", "Balen");
        await using var scope = await app.TenantScopeAsync("hopsakee");

        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ConfirmDeliveryPointAsync(reservation.Id, new GeoPoint(123, 5), ReservationActor.Website));
    }

    [Fact]
    public async Task Without_a_map_key_the_day_still_works_and_says_there_is_no_map()
    {
        var day = TestReservations.FutureDay(337);
        await ReserveAsync("Kaat", "Kaartloos", day, day, "Markt 3", "2400", "Mol");
        var client = await app.LoggedInClientAsync("kaart-zonder-sleutel@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        Assert.Contains("Kaat Kaartloos", html);
        Assert.Contains("Geen kaart", html);
        Assert.DoesNotContain("atlas.microsoft.com", html);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task The_map_shows_the_stops_of_both_rounds_and_the_business_address(UserRole role)
    {
        var day = TestReservations.FutureDay(role == UserRole.Staff ? 338 : 339);
        var delivery = await ReserveAsync("Lotte", "Levering", day, day.AddDays(2), "Markt 4", "2400", "Mol");
        var collection = await ReserveAsync("Olaf", "Ophaling", day.AddDays(-1), day, "Markt 5", "2400", "Mol");
        var lost = await ReserveAsync("Nele", "Nergens", day, day.AddDays(2), "Ergens", "2400", "Mol");
        var business = new Address { Street = "Industrieweg 10", PostalCode = "2440", City = "Geel" };
        await MoveBusinessAsync(business);
        var maps = app.Services.GetRequiredService<IMapService>();
        await using var withMap = app.WithWebHostBuilder(b => b.UseSetting("Maps:AzureMapsKey", "test-sleutel"));
        var client = await LoggedInClientAsync(withMap, $"kaart-{role}@hopsakee.test".ToLowerInvariant(), role);

        var html = await PlanningAsync(client, day);

        var map = Map(html);
        Assert.Contains($"data-bedrijf=\"{await maps.FindAsync(business)}\"", map);
        Assert.Contains($"\"nummer\":\"{delivery.Number}\",\"klant\":\"Lotte Levering\",\"ronde\":\"levering\",\"punt\":\"{delivery.DeliveryPoint}\"", map);
        Assert.Contains($"\"nummer\":\"{collection.Number}\",\"klant\":\"Olaf Ophaling\",\"ronde\":\"ophaling\",\"punt\":\"{collection.DeliveryPoint}\"", map);
        Assert.DoesNotContain(lost.Number, map);
        Assert.DoesNotContain("Geen kaart", html);
        Assert.DoesNotContain("test-sleutel", html);
    }

    [Fact]
    public async Task A_new_business_address_gets_a_new_point()
    {
        var day = TestReservations.FutureDay(340);
        await ReserveAsync("Bram", "Bedrijf", day, day, "Markt 6", "2400", "Mol");
        await using var withMap = app.WithWebHostBuilder(b => b.UseSetting("Maps:AzureMapsKey", "test-sleutel"));
        var client = await LoggedInClientAsync(withMap, "kaart-verhuisd@hopsakee.test", UserRole.Administrator);
        var moved = new Address { Street = "Nieuwe Baan 3", PostalCode = "3000", City = "Leuven" };
        await MoveBusinessAsync(new Address { Street = "Oude Baan 1", PostalCode = "2400", City = "Mol" });
        await PlanningAsync(client, day);

        await MoveBusinessAsync(moved);
        var map = Map(await PlanningAsync(client, day));

        Assert.Contains($"data-bedrijf=\"{await app.Services.GetRequiredService<IMapService>().FindAsync(moved)}\"", map);
    }

    /// <summary>Bewaart een nieuw bedrijfsadres in Bedrijfsgegevens.</summary>
    private async Task MoveBusinessAsync(Address address)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
        await details.SaveAsync(await details.GetAsync() with { Street = address.Street, PostalCode = address.PostalCode, City = address.City });
    }

    /// <summary>Een browser, ingelogd bij Hopsakee.fun, op een variant van de app (bv. met een sleutel voor de kaart).</summary>
    private async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory, string email, UserRole role)
    {
        await app.CreateUserAsync("hopsakee", email, role);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{MoshtarApp.HopsakeeHost}"), AllowAutoRedirect = false });
        await BackOffice.LoginAsync(client, email, MoshtarApp.Password);
        return client;
    }

    /// <summary>Het element van de kaart, met zijn data-attributen.</summary>
    private static string Map(string html)
    {
        var start = html.IndexOf("id=\"planning-kaart\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "Geen kaart gevonden.");
        return html[start..html.IndexOf('>', start)];
    }

    private async Task<IReadOnlyList<ReservationHistoryEntry>> HistoryAsync(Guid id)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(id);
    }

    private async Task<Reservation> ReservationAsync(Guid id)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private sealed class UnreachableMaps : IMapService
    {
        public Task<GeoPoint?> FindAsync(Address address, CancellationToken ct = default) =>
            throw new HttpRequestException("Azure Maps antwoordt niet.");
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
        var item = await ItemAsync(scope);
        var address = new Address { Street = street, PostalCode = postalCode, City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(start, end),
            [new ReservationLineRequest(item, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    /// <summary>Een artikel met ruime voorraad, aangemaakt bij eerste gebruik.</summary>
    private static async Task<Guid> ItemAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == "springkasteel");
        if (item is null)
        {
            item = new RentalItem { Slug = "springkasteel", Stock = 50, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = "Springkasteel" }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        return item.Id;
    }
}
