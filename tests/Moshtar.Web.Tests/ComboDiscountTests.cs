using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Carts;
using Moshtar.Application.Prices;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Reservations;
using Moshtar.Application.Schedule;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een combinatiekorting werkt overal met dezelfde prijsberekening: als eigen lijn met de naam van de regel in de
/// winkelkar, bij reserveren (website en back office), in de bevestigingsmail, in Open winkelkarren, de herinneringsmail
/// en GA4, en als zin op de artikelpagina.
/// </summary>
public sealed class ComboDiscountTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task The_cart_shows_the_discount_line_and_the_total_with_discount()
    {
        await NewTenantAsync("combo-winkelkar");
        var client = app.CreateClient("combo-winkelkar.test");
        var query = $"?van={TestReservations.FutureDay(10):yyyy-MM-dd}";

        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);

        var cart = await PageAsync(client, "/winkelkar");
        // Het piratenkasteel (100 euro) is het goedkoopste: 40 euro korting.
        Assert.Contains("2e springkasteel 40% korting", cart);
        Assert.Contains("−€ 40,00", cart);
        Assert.Contains("Totaal: € 180,00", cart);
    }

    [Fact]
    public async Task A_reservation_via_the_website_keeps_the_discount_line_in_the_language_of_the_reservation()
    {
        await NewTenantAsync("combo-website");
        var client = app.CreateClient("combo-website.test");
        var query = $"?van={TestReservations.FutureDay(20):yyyy-MM-dd}";
        await AddAsync(client, "/fr/louer/prinses" + query);
        await AddAsync(client, "/fr/louer/piraat" + query);
        await IdentifyAsync(client, "client@example.test", cartPath: "/fr/panier");

        var response = await WebsiteCart.ReserveAsync(client, "Claire", "/fr/panier");
        response.EnsureSuccessStatusCode();

        var reservation = await SingleReservationAsync("combo-website");
        var discount = Assert.Single(reservation.Lines, l => l.IsDiscount);
        Assert.Equal(("2e château gonflable -40%", 1, -40m), (discount.Description, discount.Quantity, discount.UnitPrice));
        Assert.Equal(180m, reservation.TotalPrice);
        Assert.Equal(reservation.Lines.Sum(l => l.LineTotal), reservation.TotalPrice);
    }

    [Fact]
    public async Task A_reservation_in_the_back_office_applies_the_rules_and_keeps_them_when_the_rule_changes_or_goes()
    {
        var tenant = await NewTenantAsync("combo-backoffice");
        var user = await app.CreateUserAsync("combo-backoffice", "medewerker@combo-backoffice.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("combo-backoffice");
        var day = TestReservations.FutureDay(30);

        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day.AddDays(1)),
            [new ReservationLineRequest(tenant.Princess, null, 1), new ReservationLineRequest(tenant.Pirate, null, 2), new ReservationLineRequest(tenant.Mill, null, 1)],
            new ReservationCustomer("Phone", "Customer", "phone@example.test", "0470 12 34 56", new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" }),
            DeliveryMethod.Delivery, null, null, "en"), ReservationActor.User(user.Id));

        // Twee dagen: prinses 120 + 60 = 180, piraat 2 × 200, molen 60. Drie springkastelen: één volledige groep, het
        // goedkoopste springkasteel (de prinses van 180) krijgt 40% korting; de goedkopere molen telt niet mee.
        var reservation = Assert.IsType<Reservation>(result.Reservation);
        var discount = Assert.Single(reservation.Lines, l => l.IsDiscount);
        Assert.Equal(("2nd bouncy castle 40% off", -72m), (discount.Description, discount.LineTotal));
        Assert.Equal(568m, reservation.TotalPrice);

        // Op de reservatie in het back office staat de korting met haar bedrag.
        var browser = app.CreateClient("combo-backoffice.test");
        await BackOffice.LoginAsync(browser, "medewerker@combo-backoffice.test", MoshtarApp.Password);
        var list = WebUtility.HtmlDecode(await browser.GetStringAsync("/admin/reservaties"));
        Assert.Contains("2nd bouncy castle 40% off: −€ 72,00", list);

        var rules = scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>();
        await rules.SaveComboAsync(tenant.Rule, new ComboDiscountInput([new("nl", "Anders")], PricingTarget.Chosen([tenant.Mill], []), 2, 10));
        await rules.DeleteAsync(tenant.Rule);

        var kept = await SingleReservationAsync("combo-backoffice");
        Assert.Equal(568m, kept.TotalPrice);
        Assert.Equal(("2nd bouncy castle 40% off", -72m), kept.Lines.Where(l => l.IsDiscount).Select(l => (l.Description, l.LineTotal)).Single());
    }

    [Fact]
    public async Task The_discount_line_is_nothing_to_load_in_the_planning_or_on_the_delivery_sheet()
    {
        var tenant = await NewTenantAsync("combo-planning");
        var user = await app.CreateUserAsync("combo-planning", "medewerker@combo-planning.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("combo-planning");
        var day = TestReservations.FutureDay(40);
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day),
            [new ReservationLineRequest(tenant.Princess, null, 1), new ReservationLineRequest(tenant.Pirate, null, 1)],
            new ReservationCustomer("Paula", "Planning", "planning@example.test", "0470 12 34 56", new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" }),
            DeliveryMethod.Delivery, null, null, "nl"), ReservationActor.User(user.Id));
        Assert.Contains(result.Reservation!.Lines, l => l.IsDiscount);

        var schedule = await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);

        var stop = Assert.Single(schedule.Deliveries);
        Assert.Equal(["Piratenkasteel", "Prinsessenkasteel"], stop.Lines.Select(l => l.Name).Order());
        Assert.Equal(["Piratenkasteel", "Prinsessenkasteel"], DeliverySheet.For(schedule, RoundKind.Delivery).LoadingList.Select(i => i.Name));
    }

    [Fact]
    public async Task The_confirmation_mail_lists_the_discount_line_and_the_total_with_discount()
    {
        await NewTenantAsync("combo-mail");
        var client = app.CreateClient("combo-mail.test");
        var query = $"?van={TestReservations.FutureDay(25):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);
        await IdentifyAsync(client, "mail@example.test");

        (await WebsiteCart.ReserveAsync(client, "Mona")).EnsureSuccessStatusCode();

        var mail = Assert.Single(Outbox.Sent, m => m.To == "mail@example.test");
        Assert.Contains("1 × 2e springkasteel 40% korting – € -40,00", mail.TextBody);
        Assert.Contains("Totaal: € 180,00", mail.TextBody);
    }

    [Fact]
    public async Task Open_carts_and_the_reminder_mail_use_the_price_with_discount()
    {
        await SeedAsync("hopsakee");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        var query = $"?van={TestReservations.FutureDay(35):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);
        await IdentifyAsync(client, "herinner-mij@example.test", reminder: true);

        var admin = await app.LoggedInClientAsync("winkelkarren-combo@hopsakee.test", UserRole.Staff);
        var openCarts = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/winkelkarren"));
        Assert.Contains("herinner-mij@example.test", openCarts);
        Assert.Contains("€ 180,00", openCarts);
        Assert.DoesNotContain("€ 220,00", openCarts);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<ICartHousekeeping>().RunAsync(DateTime.UtcNow.AddHours(25));
        var reminder = Assert.Single(Outbox.Sent, m => m.To == "herinner-mij@example.test");
        Assert.Contains("2e springkasteel 40% korting – € -40,00", reminder.TextBody);
        Assert.Contains("Totaal: € 180,00", reminder.TextBody);
    }

    [Fact]
    public async Task Google_analytics_gets_the_value_with_discount()
    {
        await NewTenantAsync("combo-ga");
        await using (var scope = await app.TenantScopeAsync("combo-ga"))
            await scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>()
                .SaveAsync(new BusinessDetailsSettings { Email = "info@combo-ga.test", GoogleAnalyticsId = "G-TEST12345" });
        var client = app.CreateClient("combo-ga.test");
        var home = await client.GetStringAsync("/");
        await client.PostAsync("/toestemming", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = BackOffice.Field(home, "__RequestVerificationToken"), ["keuze"] = "ja", ["terug"] = "/",
        }));
        var query = $"?van={TestReservations.FutureDay(45):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);

        var cart = await client.GetStringAsync("/winkelkar");
        Assert.StartsWith("{\"value\":180.00,\"currency\":\"EUR\"", Event(cart, "view_cart"));

        await IdentifyAsync(client, "ga@example.test");
        var reserved = await WebsiteCart.ReserveAsync(client, "Gust");
        var purchase = Event(await reserved.Content.ReadAsStringAsync(), "purchase");
        Assert.Matches("^\\{\"transaction_id\":\"[^\"]+\",\"value\":180.00,\"currency\":\"EUR\",\"items\":\\[", purchase);
        // De kortingslijn is geen artikel: ze zit in de waarde, niet tussen de items.
        Assert.DoesNotContain("korting", purchase);
    }

    [Theory]
    [InlineData("/huren/piraat", "Samen voordeliger: 2e springkasteel 40% korting")]
    [InlineData("/fr/louer/piraat", "Plus avantageux ensemble : 2e château gonflable -40%")]
    [InlineData("/en/rent/piraat", "Better together: 2nd bouncy castle 40% off")]
    public async Task The_item_page_names_the_combo_discount_in_the_language_of_the_page(string path, string sentence)
    {
        var slug = "combo-zin-" + (path.StartsWith("/fr/") ? "fr" : path.StartsWith("/en/") ? "en" : "nl");
        await NewTenantAsync(slug);

        var html = await PageAsync(app.CreateClient($"{slug}.test"), path);

        Assert.Contains(sentence, html);
    }

    [Fact]
    public async Task The_item_page_has_no_sentence_outside_the_rule_or_when_the_rule_is_off()
    {
        var tenant = await NewTenantAsync("combo-zin-niet");
        var client = app.CreateClient("combo-zin-niet.test");

        Assert.DoesNotContain("Samen voordeliger", await PageAsync(client, "/huren/molen"));

        await using (var scope = await app.TenantScopeAsync("combo-zin-niet"))
            await scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>().SetActiveAsync(tenant.Rule, false);
        Assert.DoesNotContain("Samen voordeliger", await PageAsync(client, "/huren/piraat"));
    }

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    /// <summary>De gegevens van een GA4-gebeurtenis op de pagina, of null als ze er niet is.</summary>
    private static string? Event(string html, string name) =>
        System.Text.RegularExpressions.Regex.Match(html, $"<script>gtag\\('event',\"{name}\",(.*?)\\);</script>") is { Success: true } m
            ? m.Groups[1].Value
            : null;

    private async Task<Reservation> SingleReservationAsync(string tenantSlug)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync();
    }

    private async Task<TestTenant> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        return await SeedAsync(slug);
    }

    /// <summary>Springkastelen (prinses 120, piraat 100), een molen (30) zonder categorie en de combinatiekorting.</summary>
    private async Task<TestTenant> SeedAsync(string slug)
    {
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", Translations = [T("nl", "Springkastelen")] };
        db.Categories.Add(castles);
        var princess = new RentalItem { Slug = "prinses", Stock = 2, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 }, Translations = [T("nl", "Prinsessenkasteel"), T("fr", "Château princesse")] };
        var pirate = new RentalItem { Slug = "piraat", Stock = 2, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Piratenkasteel"), T("fr", "Château pirate")] };
        var mill = new RentalItem { Slug = "molen", Stock = 2, Pricing = new Pricing { DayPrice = 30 }, Translations = [T("nl", "Molen")] };
        db.AddRange(princess, pirate, mill);
        await db.SaveChangesAsync();
        var rule = await scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>().CreateComboAsync(new ComboDiscountInput(
            [new("nl", "2e springkasteel 40% korting"), new("fr", "2e château gonflable -40%"), new("en", "2nd bouncy castle 40% off")],
            PricingTarget.Categories(castles.Id), MinimumQuantity: 2, Percentage: 40));
        return new TestTenant(princess.Id, pirate.Id, mill.Id, rule);
    }

    private sealed record TestTenant(Guid Princess, Guid Pirate, Guid Mill, Guid Rule);

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
