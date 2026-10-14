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
/// De Planning per week (maandag tot zondag, per dag de leveringen en ophalingen kort) en per maand (per dag enkel tellers).
/// Een klik op een dag opent de dagweergave.
/// </summary>
public sealed class ScheduleWeekMonthTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task The_week_shows_monday_to_sunday_with_the_deliveries_and_collections_of_each_day()
    {
        var monday = Monday(TestReservations.FutureDay(100));
        var wednesday = monday.AddDays(2);
        await ReserveAsync("Lien", "Peeters", wednesday, monday.AddDays(5), "Mol", "piraat");
        await ReserveAsync("Jef", "Claes", monday.AddDays(-3), wednesday, "Geel", "draak");
        var client = await app.LoggedInClientAsync("week-inhoud@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, "week", wednesday);

        for (var i = 0; i < 7; i++)
            Assert.Contains($"data-day=\"{monday.AddDays(i):yyyy-MM-dd}\"", html);
        Assert.DoesNotContain($"data-day=\"{monday.AddDays(-1):yyyy-MM-dd}\"", html);
        Assert.DoesNotContain($"data-day=\"{monday.AddDays(7):yyyy-MM-dd}\"", html);
        Assert.True(html.IndexOf("maandag", StringComparison.Ordinal) < html.IndexOf("zondag", StringComparison.Ordinal));

        var wed = Day(html, wednesday);
        Assert.Contains("Lien Peeters", Part(wed, "leveringen"));
        Assert.Contains("Mol", Part(wed, "leveringen"));
        Assert.Contains("1 × piraat", Part(wed, "leveringen"));
        Assert.Contains("Jef Claes", Part(wed, "ophalingen"));
        Assert.Contains("Geel", Part(wed, "ophalingen"));
        Assert.Contains("Lien Peeters", Part(Day(html, monday.AddDays(5)), "ophalingen"));
        Assert.DoesNotContain("Lien Peeters", Day(html, monday.AddDays(3)));
    }

    [Fact]
    public async Task The_month_counts_deliveries_and_collections_per_day_without_cancelled_and_with_pickups_apart()
    {
        var first = FirstOfMonth(TestReservations.FutureDay(130));
        var busy = first.AddDays(9);
        await ReserveAsync("Anna", "Een", busy, busy.AddDays(2), "Mol", "ballenbad");
        await ReserveAsync("Bert", "Twee", busy, busy, "Mol", "ballenbad");
        await ReserveAsync("Carl", "Drie", busy.AddDays(-1), busy, "Mol", "ballenbad");
        await ReserveAsync("Dina", "Afhaler", busy, busy.AddDays(1), "Mol", "ballenbad", DeliveryMethod.Pickup);
        var cancelled = await ReserveAsync("Erik", "Geannuleerd", busy, busy.AddDays(4), "Mol", "ballenbad");
        await ChangeStatusAsync(cancelled.Id, ReservationStatus.Cancelled);
        var client = await app.LoggedInClientAsync("maand-tellers@hopsakee.test", UserRole.Staff);

        var html = await PlanningAsync(client, "maand", busy);

        var days = DateTime.DaysInMonth(first.Year, first.Month);
        for (var i = 0; i < days; i++)
            Assert.Contains($"data-day=\"{first.AddDays(i):yyyy-MM-dd}\"", html);
        Assert.DoesNotContain($"data-day=\"{first.AddDays(-1):yyyy-MM-dd}\"", html);
        Assert.DoesNotContain($"data-day=\"{first.AddDays(days):yyyy-MM-dd}\"", html);

        var counts = Day(html, busy);
        Assert.Contains("2 leveringen", counts);
        Assert.Contains("2 ophalingen", counts);
        Assert.Contains("1 komt afhalen", counts);
        Assert.DoesNotContain("Anna", counts);
        Assert.Contains("1 levering", Day(html, busy.AddDays(-1)));
        Assert.DoesNotContain("ophaling", Day(html, busy.AddDays(-1)));
        Assert.Contains("1 brengt terug", Day(html, busy.AddDays(1)));
        Assert.Contains("1 ophaling", Day(html, busy.AddDays(2)));
        Assert.DoesNotContain("levering", Day(html, busy.AddDays(4)));
        Assert.DoesNotContain("ophaling", Day(html, busy.AddDays(4)));
    }

    [Fact]
    public async Task Switching_views_keeps_the_chosen_date_and_a_click_on_a_day_opens_that_day()
    {
        var day = TestReservations.FutureDay(160);
        var client = await app.LoggedInClientAsync("weergave-wisselen@hopsakee.test", UserRole.Staff);

        var dayView = await PlanningAsync(client, "dag", day);
        var week = await PlanningAsync(client, "week", day);
        var month = await PlanningAsync(client, "maand", day);

        foreach (var html in new[] { dayView, week, month })
        {
            Assert.Contains($"href=\"admin/planning?dag={day:yyyy-MM-dd}\"", html);
            Assert.Contains($"href=\"admin/planning?weergave=week&dag={day:yyyy-MM-dd}\"", html);
            Assert.Contains($"href=\"admin/planning?weergave=maand&dag={day:yyyy-MM-dd}\"", html);
        }
        var sunday = Monday(day).AddDays(6);
        Assert.Contains($"href=\"admin/planning?dag={sunday:yyyy-MM-dd}\"", Day(week, sunday));
        var last = FirstOfMonth(day).AddMonths(1).AddDays(-1);
        Assert.Contains($"href=\"admin/planning?dag={last:yyyy-MM-dd}\"", Day(month, last));
        Assert.Contains($"href=\"admin/planning?weergave=week&dag={day.AddDays(-7):yyyy-MM-dd}\"", week);
        Assert.Contains($"href=\"admin/planning?weergave=week&dag={day.AddDays(7):yyyy-MM-dd}\"", week);
        Assert.Contains("Volgende week", week);
        Assert.Contains($"href=\"admin/planning?weergave=maand&dag={day.AddMonths(-1):yyyy-MM-dd}\"", month);
        Assert.Contains($"href=\"admin/planning?weergave=maand&dag={day.AddMonths(1):yyyy-MM-dd}\"", month);
        Assert.Contains("Volgende maand", month);
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    private static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static async Task<string> PlanningAsync(HttpClient client, string view, DateOnly day)
    {
        var response = await client.GetAsync($"/admin/planning?weergave={view}&dag={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Het vak van één dag in de week of maand, tot het volgende vak.</summary>
    private static string Day(string html, DateOnly day)
    {
        var start = html.IndexOf($"data-day=\"{day:yyyy-MM-dd}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Dag {day} niet gevonden.");
        var end = html.IndexOf("data-day=", start + 1, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    /// <summary>Het deel van een dagvak met deze data-part (leveringen, ophalingen, afhalen, terugbrengen).</summary>
    private static string Part(string day, string part)
    {
        var start = day.IndexOf($"data-part=\"{part}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Deel {part} niet gevonden.");
        var end = day.IndexOf("data-part=", start + 1, StringComparison.Ordinal);
        return end < 0 ? day[start..] : day[start..end];
    }

    private async Task<Reservation> ReserveAsync(
        string firstName, string lastName, DateOnly start, DateOnly end, string city, string itemSlug,
        DeliveryMethod method = DeliveryMethod.Delivery)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var item = await ItemAsync(scope, itemSlug);
        var address = new Address { Street = "Dorpsstraat 1", PostalCode = "2400", City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(start, end), [new ReservationLineRequest(item, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            method, method == DeliveryMethod.Delivery ? address : null, null, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }

    private async Task ChangeStatusAsync(Guid id, ReservationStatus status)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(id, status, ReservationActor.Website);
    }

    /// <summary>Een artikel met ruime voorraad, aangemaakt bij eerste gebruik.</summary>
    private static async Task<Guid> ItemAsync(AsyncServiceScope scope, string slug)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == slug);
        if (item is null)
        {
            item = new RentalItem { Slug = slug, Stock = 20, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = slug }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        return item.Id;
    }
}
