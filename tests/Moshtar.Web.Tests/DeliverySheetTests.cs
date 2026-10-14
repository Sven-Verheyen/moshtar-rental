using System.Net;
using Microsoft.AspNetCore.TestHost;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De Leverlijst: het printblad van één ronde, met bovenaan de laadlijst en daaronder de stops in volgorde.
/// </summary>
public sealed class DeliverySheetTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Business = new() { Street = "Grote Markt 1", PostalCode = "7000", City = "Bergen" };

    [Fact]
    public async Task The_loading_list_totals_each_article_with_bundles_opened_up()
    {
        var day = TestReservations.FutureDay(230);
        var bundle = await BundleAsync("laad-feestpakket", "Laadfeestpakket", ("laad-popcorn", "Laadpopcorn", 1), ("laad-kussen", "Laadkussen", 2));
        await ReserveAsync("Lotte", "Laden", day, day.AddDays(1), "Meir 1", "2000", "Antwerpen", [(null, bundle, 2)]);
        await ReserveAsync("Lars", "Laden", day, day.AddDays(1), "Veldstraat 1", "9000", "Gent",
            [(await ItemAsync("laad-kussen", "Laadkussen"), null, 1), (await ItemAsync("laad-kar", "Laadkar"), null, 3)]);
        var client = await app.LoggedInClientAsync("leverlijst-laden@hopsakee.test", UserRole.Staff);

        var sheet = await SheetAsync(client, day, "levering");

        Assert.Equal(new Dictionary<string, int> { ["Laadkar"] = 3, ["Laadkussen"] = 5, ["Laadpopcorn"] = 2 }, LoadingList(sheet));
        Assert.DoesNotContain("Laadfeestpakket", Section(sheet, "laadlijst"));
    }

    [Fact]
    public async Task Without_a_route_the_stops_are_on_postal_code_without_times_or_map()
    {
        var day = TestReservations.FutureDay(231);
        var gent = await ReserveAsync("Zeno", "Zonderroute", day, day.AddDays(1), "Veldstraat 1", "9000", "Gent");
        var brussel = await ReserveAsync("Yves", "Zonderroute", day, day.AddDays(1), "Wetstraat 1", "1000", "Brussel");
        var mechelen = await ReserveAsync("Xavier", "Zonderroute", day, day.AddDays(1), "Bruul 1", "2800", "Mechelen");
        var client = await app.LoggedInClientAsync("leverlijst-postcode@hopsakee.test", UserRole.Staff);

        var sheet = await SheetAsync(client, day, "levering");

        Assert.Equal([brussel.Number, mechelen.Number, gent.Number], Stops(sheet));
        Assert.DoesNotContain("data-window=", sheet);
        Assert.DoesNotContain("id=\"routekaart\"", sheet);
    }

    [Fact]
    public async Task With_a_route_the_stops_follow_the_round_with_their_window_and_a_route_map_below()
    {
        var day = TestReservations.FutureDay(232);
        await MoveBusinessAsync(Business);
        var gent = await ReserveAsync("Greet", "Metroute", day, day.AddDays(1), "Veldstraat 10", "9000", "Gent");
        var antwerpen = await ReserveAsync("Anton", "Metroute", day, day.AddDays(1), "Meir 10", "2000", "Antwerpen");
        var namen = await ReserveAsync("Nico", "Metroute", day, day.AddDays(1), "Rue de Fer 10", "5000", "Namen");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        // Namen eerst: dan rijdt de snelste rest via Antwerpen naar Gent, tegen de postcodevolgorde in.
        await PlanAsync(async p => await p.MoveAsync(day, RoundKind.Delivery, namen.Id, 1));
        await PlanAsync(async p => await p.MoveAsync(day, RoundKind.Delivery, antwerpen.Id, 2));
        var client = await app.LoggedInClientAsync("leverlijst-route@hopsakee.test", UserRole.Staff);

        var sheet = await SheetAsync(client, day, "levering");

        Assert.Equal([namen.Number, antwerpen.Number, gent.Number], Stops(sheet));
        var planning = await PlanningAsync(client, day);
        foreach (var number in Stops(sheet))
            Assert.Equal(Window(planning, number), Window(Section(sheet, "stops"), number));
        Assert.Contains("Vertrek 8u00", sheet);
        var map = Section(sheet, "routekaart");
        Assert.Contains("<svg", map);
        Assert.Equal(3, Regex.Matches(map, "data-stop=").Count);
    }

    [Theory]
    [InlineData(UserRole.Staff, 240)]
    [InlineData(UserRole.Administrator, 245)]
    public async Task The_collection_round_has_its_own_sheet_from_the_planning_with_every_detail_and_a_box_to_tick(UserRole role, int offset)
    {
        var day = TestReservations.FutureDay(offset);
        var bundle = await BundleAsync($"ophaal-pakket-{offset}", "Ophaalpakket", ("ophaal-kussen", "Ophaalkussen", 2));
        var collection = await ReserveAsync("Olga", "Ophaling", day.AddDays(-2), day, "Kerkstraat 3", "2400", "Mol", [(null, bundle, 1)], "Sleutel bij de buren");
        await ReserveAsync("Lewis", "Levering", day, day.AddDays(2), "Markt 9", "2300", "Turnhout");
        var client = await app.LoggedInClientAsync($"leverlijst-ophaling-{role}@hopsakee.test".ToLowerInvariant(), role);

        var planning = await PlanningAsync(client, day);
        Assert.Contains($"href=\"admin/planning/leverlijst?dag={day:yyyy-MM-dd}&ronde=levering\"", planning);
        Assert.Contains($"href=\"admin/planning/leverlijst?dag={day:yyyy-MM-dd}&ronde=ophaling\"", planning);
        var sheet = await SheetAsync(client, day, "ophaling");

        Assert.Contains("Leverlijst ophaalronde", sheet);
        Assert.Equal([collection.Number], Stops(sheet));
        var stop = Section(sheet, "stops");
        Assert.Contains("Ophalen", stop);
        Assert.Contains("Olga Ophaling", stop);
        Assert.Contains("+32470123456", stop);
        Assert.Contains("Kerkstraat 3, 2400 Mol", stop);
        Assert.Contains("1 × Ophaalpakket (2 × Ophaalkussen)", stop);
        Assert.Contains("Sleutel bij de buren", stop);
        Assert.Contains("leverlijst-check", stop);
        Assert.Equal(new Dictionary<string, int> { ["Ophaalkussen"] = 2 }, LoadingList(sheet));
        Assert.DoesNotContain("href=\"admin/reservaties\"", sheet);
    }

    [Fact]
    public async Task Without_a_day_the_sheet_is_for_today_in_belgium()
    {
        // 23u30 UTC is al de volgende dag in België.
        var now = new DateTimeOffset(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);
        await using var later = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        await app.CreateUserAsync("hopsakee", "leverlijst-vandaag@hopsakee.test", UserRole.Staff);
        var client = later.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{MoshtarApp.HopsakeeHost}"), AllowAutoRedirect = false,
        });
        await BackOffice.LoginAsync(client, "leverlijst-vandaag@hopsakee.test", MoshtarApp.Password);

        var response = await client.GetAsync("/admin/planning/leverlijst?ronde=levering");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("woensdag 11 november 2026", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Het verwacht tijdsvenster bij een stop, het eerste data-window na haar nummer.</summary>
    private static string Window(string html, string number)
    {
        var start = html.IndexOf($"data-reservation=\"{number}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Reservatie {number} niet gevonden.");
        var match = Regex.Match(html[start..], "data-window=\"([^\"]+)\"");
        Assert.True(match.Success, $"Geen venster bij {number}.");
        return match.Groups[1].Value;
    }

    private static async Task<string> PlanningAsync(HttpClient client, DateOnly day)
    {
        var response = await client.GetAsync($"/admin/planning?dag={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task PlanAsync(Func<IRoundPlanner, Task> plan)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await plan(scope.ServiceProvider.GetRequiredService<IRoundPlanner>());
    }

    /// <summary>Bewaart een nieuw bedrijfsadres in Bedrijfsgegevens.</summary>
    private async Task MoveBusinessAsync(Address address)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
        await details.SaveAsync(await details.GetAsync() with { Street = address.Street, PostalCode = address.PostalCode, City = address.City });
    }

    /// <summary>De reservatienummers van de stops op de leverlijst, van boven naar onder.</summary>
    private static List<string> Stops(string sheet) =>
        Regex.Matches(Section(sheet, "stops"), "data-reservation=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

    /// <summary>De laadlijst als artikel → aantal.</summary>
    private static Dictionary<string, int> LoadingList(string sheet) =>
        Regex.Matches(Section(sheet, "laadlijst"), "data-article=\"([^\"]+)\" data-quantity=\"(\\d+)\"")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

    private static async Task<string> SheetAsync(HttpClient client, DateOnly day, string round)
    {
        var response = await client.GetAsync($"/admin/planning/leverlijst?dag={day:yyyy-MM-dd}&ronde={round}");
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

    /// <summary>Een pakket met deze artikelen.</summary>
    private async Task<Guid> BundleAsync(string slug, string name, params (string Slug, string Name, int Quantity)[] items)
    {
        var contents = new List<BundleItem>();
        foreach (var item in items)
            contents.Add(new BundleItem { RentalItemId = await ItemAsync(item.Slug, item.Name), Quantity = item.Quantity });
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle { Slug = slug, Pricing = new Pricing { DayPrice = 250 }, Translations = [new Translation { Culture = "nl", Name = name }], Items = contents };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    /// <summary>Een artikel met ruime voorraad, aangemaakt bij eerste gebruik.</summary>
    private async Task<Guid> ItemAsync(string slug, string name)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == slug);
        if (item is null)
        {
            item = new RentalItem { Slug = slug, Stock = 50, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = name }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        return item.Id;
    }

    private async Task<Reservation> ReserveAsync(
        string firstName, string lastName, DateOnly start, DateOnly end, string street, string postalCode, string city,
        (Guid? Item, Guid? Bundle, int Quantity)[]? lines = null, string? notes = null)
    {
        lines ??= [(await ItemAsync("laad-springkasteel", "Springkasteel"), null, 1)];
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var address = new Address { Street = street, PostalCode = postalCode, City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(start, end),
            lines.Select(l => new ReservationLineRequest(l.Item, l.Bundle, l.Quantity)).ToList(),
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, notes, "nl"), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }
}
