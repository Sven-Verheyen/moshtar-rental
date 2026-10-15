using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Prices;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een actie werkt met dezelfde prijsberekening als de combinatiekorting: als kortingslijn met haar naam in de
/// winkelkar, de reservatie, de mail en het back office. Op de reservatiewebsite tonen artikelkaart en artikelpagina de
/// oude prijs doorstreept met het label "Actie −10%"; zonder gekozen datum alleen als de actie niet aan huurperiodes
/// gebonden is. De structured data krijgt de actieprijs.
/// </summary>
public sealed partial class PromotionTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task The_card_shows_the_old_price_struck_through_and_the_promotion_label_without_a_date()
    {
        await NewTenantAsync("actie-kaart");

        var home = await PageAsync(app.CreateClient("actie-kaart.test"), "/");

        // Prinses (120) en piraat (100) zijn springkastelen: 10% actie. De molen niet.
        Assert.Equal(["€ 100,00 € 90,00 per dag Actie −10%", "€ 120,00 € 108,00 per dag Actie −10%", "€ 30,00 per dag"], Prices(home).Order());
        Assert.Contains("<s class=\"old-price\">€ 120,00</s>", home);
    }

    [Fact]
    public async Task With_a_date_the_card_and_the_item_page_show_the_promotion_price_for_the_rental_period()
    {
        await NewTenantAsync("actie-datum");
        var client = app.CreateClient("actie-datum.test");
        var day = TestReservations.FutureDay(10);
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";

        // Twee dagen prinses: 120 + 60 = 180, met 10% actie 162.
        Assert.Contains("€ 180,00 € 162,00 voor 2 dagen Actie −10%", Prices(await PageAsync(client, "/" + query)));
        var detail = await PageAsync(client, "/huren/prinses" + query);
        Assert.Contains("Totaal: € 180,00 € 162,00 voor 2 dagen, per stuk Actie −10%", Prices(detail));
    }

    [Theory]
    [InlineData("/huren/prinses", "€ 120,00 € 108,00 per dag Actie −10%")]
    [InlineData("/fr/louer/prinses", "€ 120,00 € 108,00 par jour Promo −10 %")]
    [InlineData("/en/rent/prinses", "€ 120,00 € 108,00 per day Sale −10%")]
    public async Task The_item_page_labels_the_promotion_in_the_language_of_the_page(string path, string price)
    {
        var slug = "actie-taal-" + (path.StartsWith("/fr/") ? "fr" : path.StartsWith("/en/") ? "en" : "nl");
        await NewTenantAsync(slug);

        var html = await PageAsync(app.CreateClient($"{slug}.test"), path);

        Assert.StartsWith(price, Prices(html)[0]);
    }

    [Fact]
    public async Task A_promotion_bound_to_rental_dates_shows_only_with_a_date_within_them()
    {
        var day = TestReservations.FutureDay(40);
        await NewTenantAsync("actie-huurperiode", rentalFrom: day, rentalUntil: day.AddDays(6));
        var client = app.CreateClient("actie-huurperiode.test");

        Assert.DoesNotContain("Actie", string.Join(" ", Prices(await PageAsync(client, "/huren/prinses"))));
        Assert.DoesNotContain("Actie", string.Join(" ", Prices(await PageAsync(client, "/"))));
        var outside = $"?van={day.AddDays(-1):yyyy-MM-dd}&tot={day:yyyy-MM-dd}";
        Assert.DoesNotContain("Actie", string.Join(" ", Prices(await PageAsync(client, "/huren/prinses" + outside))));

        var inside = $"?van={day:yyyy-MM-dd}";
        Assert.Contains("€ 120,00 € 108,00 voor 1 dag Actie −10%", Prices(await PageAsync(client, "/" + inside)));
        Assert.Contains("Totaal: € 120,00 € 108,00 voor 1 dag, per stuk Actie −10%", Prices(await PageAsync(client, "/huren/prinses" + inside)));
    }

    [Fact]
    public async Task Structured_data_carries_the_promotion_price()
    {
        await NewTenantAsync("actie-seo");

        var html = await PageAsync(app.CreateClient("actie-seo.test"), "/huren/prinses");

        var offer = JsonLdScript().Matches(html)
            .Select(m => JsonDocument.Parse(m.Groups[1].Value).RootElement)
            .Single(e => e.GetProperty("@type").GetString() == "Product")
            .GetProperty("offers");
        Assert.Equal("108.00", offer.GetProperty("price").GetString());
        Assert.Equal("108.00", offer.GetProperty("priceSpecification").GetProperty("price").GetString());
    }

    [Fact]
    public async Task The_promotion_is_a_discount_line_in_the_cart_the_reservation_the_mail_and_the_back_office()
    {
        await NewTenantAsync("actie-lijnen");
        var client = app.CreateClient("actie-lijnen.test");
        var query = $"?van={TestReservations.FutureDay(20):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query);
        await AddAsync(client, "/huren/piraat" + query);

        // 10% van 120 en van 100: één kortingslijn van 22 euro.
        var cart = await PageAsync(client, "/winkelkar");
        Assert.Contains("Herfstactie", cart);
        Assert.Contains("−€ 22,00", cart);
        Assert.Contains("Totaal: € 198,00", cart);

        await IdentifyAsync(client, "actie@example.test");
        (await WebsiteCart.ReserveAsync(client, "Anna")).EnsureSuccessStatusCode();

        await using (var scope = await app.TenantScopeAsync("actie-lijnen"))
        {
            var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking().SingleAsync();
            var discount = Assert.Single(reservation.Lines, l => l.IsDiscount);
            Assert.Equal(("Herfstactie", -22m), (discount.Description, discount.LineTotal));
            Assert.Equal(198m, reservation.TotalPrice);
        }

        var mail = Assert.Single(app.Services.GetRequiredService<RecordingMailTransport>().Sent, m => m.To == "actie@example.test");
        Assert.Contains("1 × Herfstactie – € -22,00", mail.TextBody);
        Assert.Contains("Totaal: € 198,00", mail.TextBody);

        await app.CreateUserAsync("actie-lijnen", "medewerker@actie-lijnen.test", UserRole.Staff);
        var browser = app.CreateClient("actie-lijnen.test");
        await BackOffice.LoginAsync(browser, "medewerker@actie-lijnen.test", MoshtarApp.Password);
        Assert.Contains("Herfstactie: −€ 22,00", WebUtility.HtmlDecode(await browser.GetStringAsync("/admin/reservaties")));
    }

    /// <summary>De tekst van elke prijs op de pagina (class "price"), zonder opmaak en met enkele spaties.</summary>
    private static List<string> Prices(string html) =>
        PriceParagraph().Matches(html)
            .Select(m => Regex.Replace(Regex.Replace(m.Groups[1].Value, "<[^>]+>", " "), "\\s+", " ").Trim())
            .ToList();

    [GeneratedRegex("<p class=\"price[^\"]*\">(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex PriceParagraph();

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdScript();

    /// <summary>
    /// Springkastelen (prinses 120 + 60 per extra dag, piraat 100), een molen (30) zonder categorie, en de actie
    /// "Herfstactie": 10% op de springkastelen, voor reservaties van gisteren tot over een maand.
    /// </summary>
    private async Task NewTenantAsync(string slug, DateOnly? rentalFrom = null, DateOnly? rentalUntil = null)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", Translations = [T("nl", "Springkastelen")] };
        db.Categories.Add(castles);
        db.AddRange(
            new RentalItem { Slug = "prinses", Stock = 2, SortOrder = 1, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 }, Translations = [T("nl", "Prinsessenkasteel"), T("fr", "Château princesse")] },
            new RentalItem { Slug = "piraat", Stock = 2, SortOrder = 2, CategoryId = castles.Id, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Piratenkasteel")] },
            new RentalItem { Slug = "molen", Stock = 2, SortOrder = 3, Pricing = new Pricing { DayPrice = 30 }, Translations = [T("nl", "Molen")] });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>().CreatePromotionAsync(new PromotionInput(
            [new("nl", "Herfstactie"), new("fr", "Action d'automne"), new("en", "Autumn sale")],
            PricingTarget.Categories(castles.Id), 10, Today.AddDays(-1), Today.AddDays(30), rentalFrom, rentalUntil));
    }

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
