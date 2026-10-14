using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De Planning per dag: de leverronde met de leveringen (begindag), de ophaalronde met de ophalingen (einddag),
/// en apart wie komt afhalen of terugbrengt.
/// </summary>
public sealed class ScheduleDayTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task A_delivery_appears_in_the_delivery_round_of_its_first_day()
    {
        var day = TestReservations.FutureDay(300);
        await ReserveAsync("Lien", "Peeters", day, day.AddDays(2), "Dorpsstraat 5", "2400", "Mol", "piraat");
        var client = await app.LoggedInClientAsync("planning-levering@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        Assert.Contains("Leverronde", html);
        Assert.Contains("Lien Peeters", Section(html, "leverronde"));
        Assert.Contains("Dorpsstraat 5, 2400 Mol", Section(html, "leverronde"));
        Assert.DoesNotContain("Lien Peeters", Section(html, "ophaalronde"));
    }

    [Fact]
    public async Task A_collection_appears_in_the_collection_round_of_its_last_day_and_a_one_day_rental_in_both()
    {
        var day = TestReservations.FutureDay(301);
        await ReserveAsync("Jef", "Claes", day.AddDays(-2), day, "Molenweg 2", "2440", "Geel", "draak");
        await ReserveAsync("Mira", "Wouters", day, day, "Veldstraat 9", "2460", "Kasterlee", "draak");
        var client = await app.LoggedInClientAsync("planning-ophaling@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        Assert.Contains("Jef Claes", Section(html, "ophaalronde"));
        Assert.DoesNotContain("Jef Claes", Section(html, "leverronde"));
        Assert.Contains("Mira Wouters", Section(html, "leverronde"));
        Assert.Contains("Mira Wouters", Section(html, "ophaalronde"));
    }

    [Fact]
    public async Task Cancelled_reservations_are_left_out_and_completed_ones_are_grey()
    {
        var day = TestReservations.FutureDay(302);
        var cancelled = await ReserveAsync("Gert", "Geannuleerd", day, day, "Kerkplein 1", "2000", "Antwerpen", "eenhoorn");
        var completed = await ReserveAsync("Ann", "Afgerond", day, day, "Kerkplein 2", "2000", "Antwerpen", "eenhoorn");
        await ChangeStatusAsync(cancelled.Id, ReservationStatus.Cancelled, ReservationActor.Website);
        await ChangeStatusAsync(completed.Id, ReservationStatus.Completed, ReservationActor.Website);
        var client = await app.LoggedInClientAsync("planning-geannuleerd@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        Assert.DoesNotContain("Gert Geannuleerd", html);
        Assert.Contains("Afgerond", Stop(Section(html, "leverronde"), completed.Number));
        Assert.Contains("planning-stop-done", Stop(Section(html, "leverronde"), completed.Number));
    }

    [Fact]
    public async Task A_stop_shows_phone_address_items_with_the_contents_of_a_bundle_notes_status_and_waze()
    {
        var day = TestReservations.FutureDay(303);
        var bundle = await BundleAsync("feestpakket", "Feestpakket", ("popcornmachine", "Popcornmachine", 1), ("springkussen", "Springkussen", 2));
        var reservation = await ReserveAsync("Sara", "Janssens", day, day.AddDays(1), "Lange Leemstraat 12", "2018", "Antwerpen",
            "feestpakket", phone: "0470 12 34 56", notes: "Bellen bij aankomst", bundleId: bundle);
        var client = await app.LoggedInClientAsync("planning-details@hopsakee.test", UserRole.Staff);

        var stop = Stop(Section(await PlanningAsync(client, day), "leverronde"), reservation.Number);

        Assert.Contains("href=\"tel:+32470123456\"", stop);
        Assert.Contains(">+32470123456<", stop);
        Assert.Contains("Lange Leemstraat 12, 2018 Antwerpen", stop);
        Assert.Contains("1 × Feestpakket", stop);
        Assert.Contains("1 × Popcornmachine", stop);
        Assert.Contains("2 × Springkussen", stop);
        Assert.Contains("Bellen bij aankomst", stop);
        Assert.Contains("Bevestigd", stop);
        Assert.Contains("href=\"https://waze.com/ul?q=Lange%20Leemstraat%2012%2C%202018%20Antwerpen&navigate=yes\"", stop.Replace("&amp;", "&"));
        Assert.Contains("Navigeer", stop);
    }

    [Fact]
    public async Task Customers_who_pick_up_themselves_are_listed_apart_without_address_or_navigation()
    {
        var day = TestReservations.FutureDay(304);
        await ReserveAsync("Koen", "Afhaler", day, day.AddDays(1), "Hoogstraat 3", "3000", "Leuven", "kar", DeliveryMethod.Pickup);
        await ReserveAsync("Bart", "Terugbrenger", day.AddDays(-1), day, "Hoogstraat 4", "3000", "Leuven", "kar", DeliveryMethod.Pickup);
        var client = await app.LoggedInClientAsync("planning-afhalen@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        var pickups = Section(html, "komt-afhalen");
        var returns = Section(html, "brengt-terug");
        Assert.Contains("Komt afhalen", pickups);
        Assert.Contains("Koen Afhaler", pickups);
        Assert.Contains("Brengt terug", returns);
        Assert.Contains("Bart Terugbrenger", returns);
        Assert.DoesNotContain("Hoogstraat", pickups + returns);
        Assert.DoesNotContain("Navigeer", pickups + returns);
        Assert.DoesNotContain("Afhaler", Section(html, "leverronde"));
        Assert.DoesNotContain("Terugbrenger", Section(html, "ophaalronde"));
    }

    [Fact]
    public async Task A_day_without_stops_says_nothing_is_planned()
    {
        var client = await app.LoggedInClientAsync("planning-leeg@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, TestReservations.FutureDay(320));

        Assert.Contains("Niets gepland", html);
    }

    [Fact]
    public async Task Stops_are_sorted_on_postal_code_until_there_is_a_route()
    {
        var day = TestReservations.FutureDay(306);
        await ReserveAsync("Zeno", "Gent", day, day, "Veldstraat 1", "9000", "Gent", "ballenbad");
        await ReserveAsync("Yves", "Brussel", day, day, "Wetstraat 1", "1000", "Brussel", "ballenbad");
        await ReserveAsync("Xavier", "Mechelen", day, day, "Bruul 1", "2800", "Mechelen", "ballenbad");
        var client = await app.LoggedInClientAsync("planning-volgorde@hopsakee.test", UserRole.Staff);

        var round = Section(await PlanningAsync(client, day), "leverronde");

        Assert.True(round.IndexOf("Yves Brussel") < round.IndexOf("Xavier Mechelen"));
        Assert.True(round.IndexOf("Xavier Mechelen") < round.IndexOf("Zeno Gent"));
    }

    [Fact]
    public async Task Previous_and_next_day_are_one_click_away()
    {
        var day = TestReservations.FutureDay(307);
        var client = await app.LoggedInClientAsync("planning-bladeren@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, day);

        Assert.Contains($"href=\"admin/planning?dag={day.AddDays(-1):yyyy-MM-dd}\"", html);
        Assert.Contains($"href=\"admin/planning?dag={day.AddDays(1):yyyy-MM-dd}\"", html);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task The_back_office_opens_on_todays_planning_with_planning_first_in_the_menu(UserRole role)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels")).DateTime);
        var client = await app.LoggedInClientAsync($"planning-start-{role}@hopsakee.test".ToLowerInvariant(), role);

        var response = await client.GetAsync("/admin");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Planning</title>", html);
        Assert.Contains($"href=\"admin/planning?dag={today.AddDays(1):yyyy-MM-dd}\"", html);
        var planning = html.IndexOf("href=\"admin/planning\"", StringComparison.Ordinal);
        Assert.True(planning >= 0, "Planning staat niet in het menu.");
        Assert.True(planning < html.IndexOf("href=\"admin/reservaties\"", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/reservaties")).StatusCode);
    }

    [Fact]
    public async Task A_delivered_stop_shows_its_status_without_the_button_and_the_history_names_the_user()
    {
        var day = TestReservations.FutureDay(308);
        var user = await app.CreateUserAsync("hopsakee", "planning-afvinker@hopsakee.test", UserRole.Staff);
        var reservation = await ReserveAsync("Dirk", "Geleverd", day, day, "Markt 1", "2300", "Turnhout", "trampoline");
        var client = await app.LoggedInClientAsync("planning-afvinken@hopsakee.test", UserRole.Staff);
        Assert.Contains("data-mark=\"Geleverd\"", Stop(Section(await PlanningAsync(client, day), "leverronde"), reservation.Number));

        await ChangeStatusAsync(reservation.Id, ReservationStatus.Delivered, ReservationActor.User(user.Id));
        var html = await PlanningAsync(client, day);

        var delivered = Stop(Section(html, "leverronde"), reservation.Number);
        Assert.Contains("Geleverd", delivered);
        Assert.DoesNotContain("data-mark=", delivered);
        Assert.Contains("data-mark=\"Teruggebracht\"", Stop(Section(html, "ophaalronde"), reservation.Number));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var history = await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(reservation.Id);
        Assert.Equal((ReservationStatus.Delivered, "planning-afvinker@hopsakee.test"), (history[0].Status, history[0].UserEmail));
    }

    private async Task ChangeStatusAsync(Guid id, ReservationStatus status, ReservationActor actor)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(id, status, actor);
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

    /// <summary>Een pakket met deze artikelen (aangemaakt bij eerste gebruik).</summary>
    private async Task<Guid> BundleAsync(string slug, string name, params (string Slug, string Name, int Quantity)[] items)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contents = new List<BundleItem>();
        foreach (var item in items)
            contents.Add(new BundleItem { RentalItemId = await ItemAsync(scope, item.Slug, item.Name), Quantity = item.Quantity });
        var bundle = new Bundle { Slug = slug, Pricing = new Pricing { DayPrice = 250 }, Translations = [new Translation { Culture = "nl", Name = name }], Items = contents };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    private async Task<Reservation> ReserveAsync(
        string firstName, string lastName, DateOnly start, DateOnly end, string street, string postalCode, string city,
        string itemSlug, DeliveryMethod method = DeliveryMethod.Delivery, string phone = "0470 12 34 56", string? notes = null, Guid? bundleId = null)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var item = await ItemAsync(scope, itemSlug);
        var address = new Address { Street = street, PostalCode = postalCode, City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(start, end),
            bundleId is { } bundle ? [new ReservationLineRequest(null, bundle, 1)] : [new ReservationLineRequest(item, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), phone, address),
            method, method == DeliveryMethod.Delivery ? address : null, notes, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    /// <summary>Een artikel met ruime voorraad, aangemaakt bij eerste gebruik.</summary>
    private static async Task<Guid> ItemAsync(AsyncServiceScope scope, string slug, string? name = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == slug);
        if (item is null)
        {
            item = new RentalItem { Slug = slug, Stock = 20, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = name ?? slug }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        return item.Id;
    }
}
