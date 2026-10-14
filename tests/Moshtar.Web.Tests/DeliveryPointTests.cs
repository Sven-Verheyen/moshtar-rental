using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Moshtar.Application.Maps;
using Moshtar.Application.Reservations;
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
