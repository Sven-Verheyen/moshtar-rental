using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Prices;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Prices;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// De rekentool op de pagina Prijsregels rekent een voorbeeldreservatie door met dezelfde prijsberekening als de
/// winkelkar: per lijn de gewone prijs, de dagen met hun periodeprijs, per stuk de korting met de naam van de regel,
/// de kortingslijnen en het totaal. Met een reservatiedatum in de toekomst geldt een latere actie. Hij bewaart niets.
/// </summary>
public sealed class PriceExampleTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task The_price_calculator_shows_what_the_cart_shows_for_the_same_choice()
    {
        var day = TestReservations.FutureDay(10);
        var (prinses, piraat) = await NewTenantAsync("rekentool-winkelkar", day.AddDays(1));
        var client = app.CreateClient("rekentool-winkelkar.test");
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);

        var result = await CalculateAsync("rekentool-winkelkar", new PriceExample(new DateRange(day, day.AddDays(1)), Today,
            [new(CatalogRef.Item(prinses), 1), new(CatalogRef.Item(piraat), 1)]));

        // Dag 2 is 50% duurder: prinses 120 + 60 × 1,5 = 210, piraat 100 + 100 × 1,5 = 250. De prinses is het
        // goedkoopste springkasteel: 40% van 210 = 84 korting. Totaal 460 − 84 = 376.
        var cart = await PageAsync(client, "/winkelkar");
        Assert.Contains("−€ 84,00", cart);
        Assert.Contains("Totaal: € 376,00", cart);

        Assert.Equal(376m, result.Total);
        Assert.Equal([new PriceExampleDiscount("2e springkasteel 40% korting", 84m, 1)], result.Discounts);
        var line = result.Lines[0];
        Assert.Equal(("Prinsessenkasteel", 1, 180m, 210m), (line.Name, line.Quantity, line.NormalPrice, line.UnitPrice));
        Assert.Equal([new PricedDay(day, 120m, 0m, 120m), new PricedDay(day.AddDays(1), 60m, 50m, 90m)], line.Days);
        Assert.Equal([new PriceExamplePiece(210m, "2e springkasteel 40% korting", 84m)], line.Pieces);
        Assert.Equal([new PriceExamplePiece(250m, null, 0m)], result.Lines[1].Pieces);
    }

    [Fact]
    public async Task A_reservation_date_in_the_future_shows_a_later_promotion_and_nothing_is_stored()
    {
        var day = TestReservations.FutureDay(30);
        var (_, piraat) = await NewTenantAsync("rekentool-actie", day.AddDays(1));
        await using (var scope = await app.TenantScopeAsync("rekentool-actie"))
            await scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>().CreatePromotionAsync(new PromotionInput(
                [new("nl", "Winteractie")], PricingTarget.Everything, 15, Today.AddDays(5), Today.AddDays(10)));
        var lines = new List<PriceExampleLine> { new(CatalogRef.Item(piraat), 1) };

        var today = await CalculateAsync("rekentool-actie", new PriceExample(new DateRange(day, day), Today, lines));
        var later = await CalculateAsync("rekentool-actie", new PriceExample(new DateRange(day, day), Today.AddDays(7), lines));
        var afterwards = await CalculateAsync("rekentool-actie", new PriceExample(new DateRange(day, day), Today.AddDays(11), lines));

        Assert.Empty(today.Discounts);
        Assert.Equal(100m, today.Total);
        // 15% van 100.
        Assert.Equal([new PriceExampleDiscount("Winteractie", 15m, 1)], later.Discounts);
        Assert.Equal(85m, later.Total);
        Assert.Empty(afterwards.Discounts);

        await using var check = await app.TenantScopeAsync("rekentool-actie");
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Reservations.AnyAsync());
        Assert.False(await db.Carts.AnyAsync());
    }

    [Fact]
    public async Task Administrator_sees_the_price_calculator_at_the_top_of_the_pricing_rules_page()
    {
        var client = await app.LoggedInClientAsync("rekentool-beheerder@hopsakee.test", UserRole.Administrator);

        var response = await client.GetAsync("/admin/prijsregels?tab=acties");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Rekentool", html);
        Assert.Contains("Reservatiedatum", html);
        Assert.True(html.IndexOf("Rekentool", StringComparison.Ordinal) < html.IndexOf("Combinatiekortingen", StringComparison.Ordinal));
        Assert.Contains("Nieuwe actie", html);
    }

    [Fact]
    public async Task The_reservation_date_starts_at_today_in_belgium()
    {
        await NewTenantAsync("rekentool-vandaag", TestReservations.FutureDay(10));
        // 23u30 UTC is al de volgende dag in België.
        var now = new DateTimeOffset(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);
        await using var later = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        await using var scope = later.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Hosts).SingleAsync(t => t.Slug == "rekentool-vandaag");
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);

        var choices = await scope.ServiceProvider.GetRequiredService<IPriceExamples>().GetChoicesAsync();

        Assert.Equal(new DateOnly(2026, 11, 11), choices.Today);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<PriceExampleResult> CalculateAsync(string slug, PriceExample example)
    {
        await using var scope = await app.TenantScopeAsync(slug);
        return await scope.ServiceProvider.GetRequiredService<IPriceExamples>().CalculateAsync(example);
    }

    /// <summary>
    /// Springkastelen prinses (120 + 60 per extra dag) en piraat (100), de combinatiekorting "2e springkasteel 40%
    /// korting" en een periodeprijs van +50% op <paramref name="busyDay"/>.
    /// </summary>
    private async Task<(Guid Prinses, Guid Piraat)> NewTenantAsync(string slug, DateOnly busyDay)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", Translations = [T("nl", "Springkastelen")] };
        db.Categories.Add(castles);
        var prinses = new RentalItem { Slug = "prinses", Stock = 2, SortOrder = 1, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 }, Translations = [T("nl", "Prinsessenkasteel")] };
        var piraat = new RentalItem { Slug = "piraat", Stock = 2, SortOrder = 2, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Piratenkasteel")] };
        db.AddRange(prinses, piraat);
        await db.SaveChangesAsync();
        var rules = scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>();
        await rules.CreateComboAsync(new ComboDiscountInput([new("nl", "2e springkasteel 40% korting")], PricingTarget.Categories(castles.Id), 2, 40));
        await rules.CreatePeriodPriceAsync(new PeriodPriceInput("Drukke dag", PricingTarget.Everything, 50, busyDay, busyDay, []));
        return (prinses.Id, piraat.Id);
    }

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
