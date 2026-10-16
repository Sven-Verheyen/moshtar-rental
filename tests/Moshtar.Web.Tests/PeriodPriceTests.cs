using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Prices;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Periodeprijzen: de beheerder beheert ze op het tabblad Periodeprijzen van de pagina Prijsregels (naam, data en/of
/// weekdagen, een percentage van −90% tot +200%, voor alles of gekozen artikelen, pakketten en categorieën, aan/uit en
/// een optionele einddatum). Op de reservatiewebsite tonen kaarten, artikelpagina, winkelkar en structured data met
/// een gekozen huurperiode de prijs mét periodeprijs, en een reservatie bewaart die prijs; zonder datum blijft de gewone prijs.
/// </summary>
public sealed class PeriodPriceTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task A_period_price_is_created_changed_switched_off_and_deleted()
    {
        await app.CreateTenantAsync("periode-beheer", "periode-beheer.test");
        await using var scope = await app.TenantScopeAsync("periode-beheer");
        var today = Belgium.Today(TimeProvider.System);
        var castles = Guid.NewGuid();

        var id = await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput(
            " Zaterdag ", PricingTarget.Everything, Percentage: 20, From: null, Until: null, Weekdays: [DayOfWeek.Saturday]));

        var saturday = Assert.Single(await Rules(scope).GetPeriodPricesAsync());
        Assert.Equal(id, saturday.Id);
        Assert.Equal(("Zaterdag", true, 20m, true, (DateOnly?)null, false),
            (saturday.Input.Name, saturday.Input.Target.All, saturday.Input.Percentage, saturday.Input.IsActive, saturday.Input.EndDate, saturday.IsExpired));
        Assert.Equal([DayOfWeek.Saturday], saturday.Input.Weekdays);

        var summer = (new DateOnly(2027, 7, 1), new DateOnly(2027, 8, 31));
        await Rules(scope).SavePeriodPriceAsync(id, new PeriodPriceInput(
            "Zomer", PricingTarget.Categories(castles), -15.5m, summer.Item1, summer.Item2, [], EndDate: today.AddDays(5)));
        var changed = Assert.Single(await Rules(scope).GetPeriodPricesAsync()).Input;
        Assert.Equal(("Zomer", false, -15.5m, summer.Item1, summer.Item2, today.AddDays(5)),
            (changed.Name, changed.Target.All, changed.Percentage, changed.From, changed.Until, changed.EndDate));
        Assert.Equal([castles], changed.Target.CategoryIds);
        Assert.Empty(changed.Weekdays);

        await Rules(scope).SetActiveAsync(id, false);
        Assert.False(Assert.Single(await Rules(scope).GetPeriodPricesAsync()).Input.IsActive);

        await Rules(scope).DeleteAsync(id);
        Assert.Empty(await Rules(scope).GetPeriodPricesAsync());
    }

    [Fact]
    public async Task Without_a_target_a_period_price_applies_to_everything_and_combo_discounts_stay_apart()
    {
        await app.CreateTenantAsync("periode-alles", "periode-alles.test");
        await using var scope = await app.TenantScopeAsync("periode-alles");
        await Rules(scope).CreateComboAsync(new ComboDiscountInput([new("nl", "Combi")], PricingTarget.Categories(Guid.NewGuid()), 2, 10));

        await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput("Zondag", PricingTarget.Chosen([], []), 10, null, null, [DayOfWeek.Sunday]));

        Assert.True(Assert.Single(await Rules(scope).GetPeriodPricesAsync()).Input.Target.All);
        Assert.Equal("Combi", Assert.Single(await Rules(scope).GetCombosAsync()).Input.Names.Single().Name);
    }

    [Fact]
    public async Task An_invalid_period_price_is_refused()
    {
        await app.CreateTenantAsync("periode-ongeldig", "periode-ongeldig.test");
        await using var scope = await app.TenantScopeAsync("periode-ongeldig");

        var refused = await Assert.ThrowsAsync<PricingRuleException>(() => Rules(scope).CreatePeriodPriceAsync(
            new PeriodPriceInput(" ", PricingTarget.Everything, -90.5m, null, null, [])));
        Assert.Equal([
            "De naam is leeg.",
            "Kies data, weekdagen of allebei.",
            "Het percentage ligt tussen −90% en +200%.",
        ], refused.Problems);

        refused = await Assert.ThrowsAsync<PricingRuleException>(() => Rules(scope).CreatePeriodPriceAsync(
            new PeriodPriceInput("Omgekeerd", PricingTarget.Everything, 200.01m, new DateOnly(2027, 8, 31), new DateOnly(2027, 7, 1), [])));
        Assert.Equal(["De begindatum ligt na de einddatum.", "Het percentage ligt tussen −90% en +200%."], refused.Problems);

        refused = await Assert.ThrowsAsync<PricingRuleException>(() => Rules(scope).CreatePeriodPriceAsync(
            new PeriodPriceInput("Niets", PricingTarget.Everything, 0, null, null, [DayOfWeek.Monday])));
        Assert.Equal(["Een periodeprijs van 0% verandert niets."], refused.Problems);
        Assert.Empty(await Rules(scope).GetPeriodPricesAsync());

        // De grenzen zelf mogen.
        await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput("Min", PricingTarget.Everything, -90, new DateOnly(2027, 1, 1), null, []));
        await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput("Max", PricingTarget.Everything, 200, null, new DateOnly(2027, 1, 1), []));
        Assert.Equal(2, (await Rules(scope).GetPeriodPricesAsync()).Count);
    }

    [Fact]
    public async Task Administrator_sees_the_period_prices_on_their_tab()
    {
        var client = await app.LoggedInClientAsync("periodeprijzen-beheerder@hopsakee.test", UserRole.Administrator);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput("Drukke zaterdag", PricingTarget.Everything, 20, null, null, [DayOfWeek.Saturday]));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/prijsregels?tab=periodeprijzen"));

        Assert.Contains("Drukke zaterdag", html);
        Assert.Contains("+20%", html);
    }

    // Met de periodeprijs "Feestweek +20%" op de eerste dag: piraat 100 × 1,20 + 50 = 170 voor twee dagen (gewoon 150).

    [Fact]
    public async Task The_item_page_and_its_structured_data_show_the_price_with_period_price_for_the_chosen_rental_period()
    {
        var day = await SeedAsync("periode-artikel", offset: 10);
        var client = app.CreateClient("periode-artikel.test");

        var html = await PageAsync(client, $"/huren/piraat?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}");
        Assert.Contains("Totaal: € 170,00", html);
        var offer = JsonLd(html, "Product").GetProperty("offers");
        Assert.Equal("170.00", offer.GetProperty("price").GetString());
        var specification = offer.GetProperty("priceSpecification");
        Assert.Equal("170.00", specification.GetProperty("price").GetString());
        Assert.Equal((2, "DAY"), (specification.GetProperty("referenceQuantity").GetProperty("value").GetInt32(),
            specification.GetProperty("referenceQuantity").GetProperty("unitCode").GetString()));

        // Zonder datum: de gewone dagprijs, ook in de structured data.
        var plain = await PageAsync(client, "/huren/piraat");
        Assert.Contains("€ 100,00", plain);
        Assert.DoesNotContain("€ 120,00", plain);
        Assert.Equal("100.00", JsonLd(plain, "Product").GetProperty("offers").GetProperty("price").GetString());
    }

    [Fact]
    public async Task Catalog_cards_show_the_price_with_period_price_for_the_chosen_rental_period()
    {
        var day = await SeedAsync("periode-kaarten", offset: 11);
        var client = app.CreateClient("periode-kaarten.test");
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";

        Assert.Contains("€ 170,00 voor 2 dagen", Text(await PageAsync(client, "/" + query)));
        Assert.Contains("€ 170,00 voor 2 dagen", Text(await PageAsync(client, "/springkastelen" + query)));
        // Een dag buiten de periodeprijs: de gewone prijs.
        var later = $"?van={day.AddDays(1):yyyy-MM-dd}&tot={day.AddDays(2):yyyy-MM-dd}";
        Assert.Contains("€ 150,00 voor 2 dagen", Text(await PageAsync(client, "/springkastelen" + later)));
    }

    [Fact]
    public async Task The_cart_and_the_reservation_carry_the_price_with_period_price_without_a_separate_line()
    {
        var day = await SeedAsync("periode-winkelkar", offset: 12);
        var client = app.CreateClient("periode-winkelkar.test");
        await AddAsync(client, $"/huren/piraat?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}", quantity: 2);

        var cart = Text(await PageAsync(client, "/winkelkar"));
        Assert.Contains("€ 170,00 voor 2 dagen", cart);
        Assert.Contains("Totaal: € 340,00", cart);
        Assert.DoesNotContain("Feestweek", cart);

        await IdentifyAsync(client, "periode@example.test");
        (await ReserveAsync(client, "Piet")).EnsureSuccessStatusCode();

        var reservation = await SingleReservationAsync("periode-winkelkar");
        var line = Assert.Single(reservation.Lines);
        Assert.Equal((2, 170m), (line.Quantity, line.UnitPrice));
        Assert.Equal(340m, reservation.TotalPrice);
    }

    [Fact]
    public async Task A_reservation_in_the_back_office_keeps_the_price_with_period_price()
    {
        var day = await SeedAsync("periode-backoffice", offset: 13);
        var user = await app.CreateUserAsync("periode-backoffice", "medewerker@periode-backoffice.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("periode-backoffice");
        var pirate = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RentalItems.SingleAsync(i => i.Slug == "piraat")).Id;

        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day.AddDays(1)), [new ReservationLineRequest(pirate, null, 1)],
            new ReservationCustomer("Phone", "Customer", "phone@example.test", "0470 12 34 56", new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" }),
            DeliveryMethod.Delivery, null, null, "nl"), ReservationActor.User(user.Id));

        var reservation = Assert.IsType<Reservation>(result.Reservation);
        Assert.Equal(170m, Assert.Single(reservation.Lines).UnitPrice);
        Assert.Equal(170m, reservation.TotalPrice);
    }

    [Fact]
    public async Task Google_analytics_gets_the_price_with_period_price_when_adding_to_the_cart()
    {
        var day = await SeedAsync("periode-ga", offset: 14);
        await using (var scope = await app.TenantScopeAsync("periode-ga"))
            await scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>()
                .SaveAsync(new BusinessDetailsSettings { Email = "info@periode-ga.test", GoogleAnalyticsId = "G-TEST12345" });
        var client = app.CreateClient("periode-ga.test");
        var home = await client.GetStringAsync("/");
        await client.PostAsync("/toestemming", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = BackOffice.Field(home, "__RequestVerificationToken"), ["keuze"] = "ja", ["terug"] = "/",
        }));

        var added = await AddAsync(client, $"/huren/piraat?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}");

        var page = await client.GetStringAsync(added.Headers.Location!.OriginalString);
        Assert.StartsWith("{\"value\":170.00,\"currency\":\"EUR\"", Event(page, "add_to_cart"));
    }

    /// <summary>
    /// Springkastelen met een piratenkasteel (100, extra dag 50) en de periodeprijs "Feestweek +20%" voor alles op de
    /// dag <paramref name="offset"/> dagen na <see cref="TestReservations.FutureDay"/>; geeft die dag terug.
    /// </summary>
    private async Task<DateOnly> SeedAsync(string slug, int offset)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", Translations = [T("Springkastelen")] };
        db.Categories.Add(castles);
        db.RentalItems.Add(new RentalItem
        {
            Slug = "piraat", Stock = 3, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 100, ExtraDayPrice = 50 }, Translations = [T("Piratenkasteel")],
        });
        await db.SaveChangesAsync();
        var day = TestReservations.FutureDay(offset);
        await Rules(scope).CreatePeriodPriceAsync(new PeriodPriceInput("Feestweek", PricingTarget.Everything, 20, day, day, []));
        return day;
    }

    private async Task<Reservation> SingleReservationAsync(string tenantSlug)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync();
    }

    /// <summary>De zichtbare tekst, zonder tags en met enkelvoudige spaties.</summary>
    private static string Text(string html) => Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), "\\s+", " ");

    /// <summary>Het JSON-LD-object van dit type in de pagina.</summary>
    private static JsonElement JsonLd(string html, string type) =>
        Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)
            .Select(m => JsonDocument.Parse(m.Groups[1].Value).RootElement)
            .Single(e => e.GetProperty("@type").GetString() == type);

    /// <summary>De gegevens van een GA4-gebeurtenis op de pagina, of null als ze er niet is.</summary>
    private static string? Event(string html, string name) =>
        Regex.Match(html, $"<script>gtag\\('event',\"{name}\",(.*?)\\);</script>") is { Success: true } m ? m.Groups[1].Value : null;

    private static Translation T(string name) => new() { Culture = "nl", Name = name };

    private static IPricingRuleAdministration Rules(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>();
}
