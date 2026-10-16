using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Mail;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// De winkelkar op de reservatiewebsite: na "Kies je datum" legt de klant artikelen en pakketten in zijn winkelkar,
/// voor één huurperiode. Op de winkelkarpagina wijzigt hij aantallen, verwijdert hij lijnen en kiest hij een andere
/// datum; wat niet meer vrij is, staat aangeduid. De winkelkar staat op de server, de klant heeft de sleutel in een cookie.
/// </summary>
public sealed class CartTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Without_a_date_the_item_page_has_no_add_to_cart_button()
    {
        await NewTenantAsync("kar-zonder-datum");

        var html = await PageAsync(app.CreateClient("kar-zonder-datum.test"), "/huren/prinses");

        Assert.Contains("Kies je datum, dan zie je meteen of het vrij is en wat het kost.", html);
        Assert.DoesNotContain("In winkelkar", html);
    }

    [Fact]
    public async Task A_customer_adds_an_item_stays_on_the_page_and_sees_it_in_the_header_and_the_cart()
    {
        await NewTenantAsync("kar-toevoegen");
        var client = app.CreateClient("kar-toevoegen.test");
        var day = TestReservations.FutureDay(10);
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(2):yyyy-MM-dd}";

        var page = await PageAsync(client, "/huren/prinses" + query);
        Assert.Contains("<button type=\"submit\" class=\"button button-large\">In winkelkar</button>", page);
        Assert.DoesNotContain("class=\"cart-count\"", page);

        var response = await AddAsync(client, "/huren/prinses" + query, quantity: 1);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/huren/prinses{query}&winkelkar=toegevoegd&aantal=1#reserveren", response.Headers.Location!.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("moshtar-winkelkar="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=604800", cookie, StringComparison.OrdinalIgnoreCase);

        var after = await PageAsync(client, response.Headers.Location.OriginalString);
        Assert.Contains("<p>Toegevoegd aan je winkelkar.</p>", after);
        Assert.Contains("<a class=\"button\" href=\"/winkelkar\">Naar winkelkar</a>", after);
        Assert.Contains("<span class=\"cart-count\">1</span>", after);
        Assert.Contains("aria-label=\"Winkelkar, 1 stuks\"", after);

        var cart = await PageAsync(client, "/winkelkar");
        Assert.Contains("<h1>Je winkelkar</h1>", cart);
        Assert.Contains($"<a href=\"/huren/prinses{query}\">Prinsessenkasteel</a>", cart);
        // Eerste dag 120 euro, twee extra dagen aan 60 euro.
        Assert.Contains("€ 240,00 voor 3 dagen", cart);
        Assert.Contains("Totaal: € 240,00", cart);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", cart);
    }

    [Fact]
    public async Task The_same_item_twice_raises_the_quantity_and_an_item_and_a_bundle_go_together()
    {
        var tenant = await NewTenantAsync("kar-samen", stock: 3);
        var client = app.CreateClient("kar-samen.test");
        var query = $"?van={TestReservations.FutureDay(20):yyyy-MM-dd}";

        await AddAsync(client, "/huren/prinses" + query, quantity: 1);
        await AddAsync(client, "/huren/prinses" + query, quantity: 2);
        await AddAsync(client, "/huren/feest" + query, quantity: 1);

        var cart = await CartAsync("kar-samen");
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(3, cart.Lines.Single(l => l.RentalItemId == tenant.Princess).Quantity);
        Assert.Equal(1, cart.Lines.Single(l => l.BundleId == tenant.Party).Quantity);
        var html = await PageAsync(client, "/winkelkar");
        Assert.Contains("<span class=\"cart-count\">4</span>", html);
        // 3 × 120 euro en een feestpakket van 150 euro.
        Assert.Contains("Totaal: € 510,00", html);
    }

    [Fact]
    public async Task What_does_not_fit_together_with_the_cart_is_not_added()
    {
        var tenant = await NewTenantAsync("kar-past-niet");
        var client = app.CreateClient("kar-past-niet.test");
        var day = TestReservations.FutureDay(30);
        var query = $"?van={day:yyyy-MM-dd}";

        // Het feestpakket bevat de enige molen: de molen los erbij past niet meer.
        await AddAsync(client, "/huren/feest" + query, quantity: 1);
        var response = await AddAsync(client, "/huren/molen" + query, quantity: 1);

        Assert.EndsWith("winkelkar=niet-vrij#reserveren", response.Headers.Location!.OriginalString);
        var html = await PageAsync(client, response.Headers.Location.OriginalString);
        Assert.Contains("Dit past niet meer in je winkelkar: samen met wat er al in zit, is er op deze datum niet genoeg vrij.", html);
        var cart = await CartAsync("kar-past-niet");
        Assert.Equal(tenant.Party, Assert.Single(cart.Lines).BundleId);
    }

    [Fact]
    public async Task A_customer_changes_the_quantity_and_removes_a_line()
    {
        var tenant = await NewTenantAsync("kar-wijzigen", stock: 5);
        var client = app.CreateClient("kar-wijzigen.test");
        var query = $"?van={TestReservations.FutureDay(40):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query, quantity: 1);
        await AddAsync(client, "/huren/piraat" + query, quantity: 1);

        var page = await PageAsync(client, "/winkelkar");
        var changed = await PostAsync(client, "/winkelkar/aantal", page, new() { ["soort"] = "artikel", ["id"] = tenant.Princess.ToString(), ["aantal"] = "4", ["terug"] = "/winkelkar" });
        await PostAsync(client, "/winkelkar/aantal", page, new() { ["soort"] = "artikel", ["id"] = tenant.Pirate.ToString(), ["aantal"] = "0", ["terug"] = "/winkelkar" });

        Assert.Equal("/winkelkar", changed.Headers.Location!.OriginalString);
        var line = Assert.Single((await CartAsync("kar-wijzigen")).Lines);
        Assert.Equal((tenant.Princess, 4), (line.RentalItemId!.Value, line.Quantity));
        var html = await PageAsync(client, "/winkelkar");
        Assert.DoesNotContain("Piratenkasteel", html);
        Assert.Contains("name=\"aantal\" value=\"4\"", html);
    }

    [Fact]
    public async Task Another_date_applies_to_the_whole_cart_and_marks_what_is_no_longer_free()
    {
        var tenant = await NewTenantAsync("kar-datum");
        var client = app.CreateClient("kar-datum.test");
        var day = TestReservations.FutureDay(50);
        var other = day.AddDays(7);
        await ReserveForSomeoneElseAsync("kar-datum", tenant.Pirate, other);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}", quantity: 1);
        await AddAsync(client, $"/huren/piraat?van={day:yyyy-MM-dd}", quantity: 1);

        var page = await PageAsync(client, "/winkelkar");
        Assert.DoesNotContain("Niet vrij op deze datum", page);
        await PostAsync(client, "/winkelkar/datum", page, new() { ["van"] = $"{other:yyyy-MM-dd}", ["tot"] = $"{other:yyyy-MM-dd}", ["terug"] = "/winkelkar" });

        var cart = await CartAsync("kar-datum");
        Assert.Equal((other, other), (cart.StartDate, cart.EndDate));
        var html = await PageAsync(client, "/winkelkar");
        Assert.Contains($"Huurperiode: {other.ToString("d MMMM yyyy", Dutch)}", html);
        Assert.Equal(1, Count(html, "<span class=\"badge badge-busy\">Niet vrij op deze datum</span>"));
        Assert.True(html.IndexOf("Piratenkasteel") < html.IndexOf("Niet vrij op deze datum"));
        Assert.Contains("Niet alles in je winkelkar is nog vrij op deze datum.", html);
    }

    [Fact]
    public async Task Adding_with_another_date_moves_the_whole_cart_to_that_date()
    {
        await NewTenantAsync("kar-andere-datum");
        var client = app.CreateClient("kar-andere-datum.test");
        var (first, second) = (TestReservations.FutureDay(60), TestReservations.FutureDay(61));

        await AddAsync(client, $"/huren/prinses?van={first:yyyy-MM-dd}", quantity: 1);
        await AddAsync(client, $"/huren/piraat?van={second:yyyy-MM-dd}", quantity: 1);

        var cart = await CartAsync("kar-andere-datum");
        Assert.Equal((second, second), (cart.StartDate, cart.EndDate));
        Assert.Equal(2, cart.Lines.Count);
    }

    [Fact]
    public async Task An_impossible_date_in_the_cart_gives_a_message_and_changes_nothing()
    {
        await NewTenantAsync("kar-datum-fout");
        var client = app.CreateClient("kar-datum-fout.test");
        var day = TestReservations.FutureDay(70);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}", quantity: 1);

        var page = await PageAsync(client, "/winkelkar");
        var response = await PostAsync(client, "/winkelkar/datum", page, new() { ["van"] = $"{day:yyyy-MM-dd}", ["tot"] = $"{day.AddDays(-1):yyyy-MM-dd}", ["terug"] = "/winkelkar" });

        Assert.Equal("/winkelkar?fout=PeriodEndBeforeStart", response.Headers.Location!.OriginalString);
        Assert.Contains("De einddag ligt voor de begindag.", await PageAsync(client, response.Headers.Location.OriginalString));
        Assert.Equal(day, (await CartAsync("kar-datum-fout")).StartDate);
    }

    [Fact]
    public async Task The_cart_page_is_in_the_language_of_the_url_and_empty_without_a_cart()
    {
        await NewTenantAsync("kar-taal");
        var client = app.CreateClient("kar-taal.test");

        var dutch = await PageAsync(client, "/winkelkar");
        var french = await PageAsync(client, "/fr/panier");
        var wrongWord = await client.GetAsync("/fr/winkelkar");

        Assert.Contains("Je winkelkar is leeg.", dutch);
        Assert.Contains("<h1>Votre panier</h1>", french);
        Assert.Contains("<a class=\"cart-link\" href=\"/fr/panier\"", french);
        Assert.Equal(HttpStatusCode.MovedPermanently, wrongWord.StatusCode);
        Assert.Equal("/fr/panier", wrongWord.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Another_website_cannot_put_something_in_the_cart_of_the_visitor()
    {
        var tenant = await NewTenantAsync("kar-vervalst");
        var client = app.CreateClient("kar-vervalst.test");

        var response = await client.PostAsync("/winkelkar/toevoegen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["soort"] = "artikel", ["id"] = tenant.Princess.ToString(), ["aantal"] = "1",
            ["van"] = $"{TestReservations.FutureDay(80):yyyy-MM-dd}", ["terug"] = "/huren/prinses",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("moshtar-winkelkar")));
        await using var scope = await app.TenantScopeAsync("kar-vervalst");
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Carts.AnyAsync());
    }

    [Fact]
    public async Task A_cart_belongs_to_one_rental_company()
    {
        await NewTenantAsync("kar-eigen");
        await NewTenantAsync("kar-ander");
        var client = app.CreateClient("kar-eigen.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(90):yyyy-MM-dd}", quantity: 1);
        var key = (await CartAsync("kar-eigen")).Key;

        // Dezelfde sleutel op het domein van een andere verhuurder vindt niets.
        var other = app.CreateClient("kar-ander.test");
        other.DefaultRequestHeaders.Add("Cookie", $"moshtar-winkelkar={key}");
        var html = await PageAsync(other, "/winkelkar");

        Assert.Contains("Je winkelkar is leeg.", html);
        Assert.DoesNotContain("class=\"cart-count\"", html);
    }

    [Fact]
    public async Task Reserving_from_the_cart_makes_one_reservation_with_one_mail_and_empties_the_cart()
    {
        var tenant = await NewTenantAsync("kar-reserveren", stock: 2);
        var client = app.CreateClient("kar-reserveren.test");
        var day = TestReservations.FutureDay(100);
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";
        await AddAsync(client, "/huren/prinses" + query, quantity: 2);
        await AddAsync(client, "/huren/feest" + query, quantity: 1);

        // Zonder e-mailadres eerst stap 1, nog geen formulier met de gegevens.
        var before = await PageAsync(client, "/winkelkar");
        Assert.Contains("Stap 1 van 2", before);
        Assert.DoesNotContain("name=\"Form.FirstName\"", before);
        var step1 = await IdentifyAsync(client, "Sara@Example.test");
        Assert.Equal("/winkelkar?stap=gegevens#reserveren", step1.Headers.Location!.OriginalString);
        var step2 = await PageAsync(client, "/winkelkar");
        Assert.Contains("Stap 2 van 2", step2);
        Assert.Contains("<strong>sara@example.test</strong>", step2);

        var response = await ReserveAsync(client, "Sara");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        // 2 prinsessenkastelen voor 2 dagen (120 + 60) en een feestpakket voor 2 dagen (150 + 150).
        Assert.Contains("is bevestigd. Totaal: € 660,00.", html);
        Assert.Contains("<h1>Bedankt voor je reservatie</h1>", html);
        Assert.DoesNotContain("class=\"cart-count\"", html);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("moshtar-winkelkar=;"));
        await using var scope = await app.TenantScopeAsync("kar-reserveren");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reservation = await db.Reservations.Include(r => r.Customer).SingleAsync();
        Assert.Equal(("sara@example.test", "Sara", ReservationStatus.Confirmed), (reservation.Customer!.Email, reservation.Customer.FirstName, reservation.Status));
        Assert.Equal(2, reservation.Lines.Count);
        Assert.Equal((day, day.AddDays(1)), (reservation.StartDate, reservation.EndDate));
        Assert.Single(app.Services.GetRequiredService<RecordingMailTransport>().Sent, m => m.To == "sara@example.test");
        var cart = await db.Carts.AsNoTracking().SingleAsync();
        Assert.Equal(reservation.Id, cart.ReservationId);
        Assert.NotNull(cart.ReservedAtUtc);
        Assert.Contains("Je winkelkar is leeg.", await PageAsync(client, "/winkelkar"));
    }

    [Theory]
    [InlineData("", "kar-tel-nl", "/huren/prinses", "/winkelkar", "Vul je telefoonnummer in, zodat we je kunnen bereiken over je reservatie.")]
    [InlineData("", "kar-tel-fr", "/fr/louer/prinses", "/fr/panier", "Indiquez votre numéro de téléphone, pour que nous puissions vous joindre au sujet de votre réservation.")]
    [InlineData("", "kar-tel-en", "/en/rent/prinses", "/en/cart", "Please enter your phone number, so we can reach you about your reservation.")]
    [InlineData("0470 12", "kar-tel-fout-nl", "/huren/prinses", "/winkelkar", "Dit is geen geldig telefoonnummer. Vul het in zoals 0470 12 34 56 of +32 470 12 34 56.")]
    [InlineData("bel me", "kar-tel-fout-fr", "/fr/louer/prinses", "/fr/panier", "Ce n'est pas un numéro de téléphone valide. Indiquez-le comme 0470 12 34 56 ou +32 470 12 34 56.")]
    [InlineData("12", "kar-tel-fout-en", "/en/rent/prinses", "/en/cart", "This is not a valid phone number. Enter it like 0470 12 34 56 or +32 470 12 34 56.")]
    public async Task Reserving_without_a_valid_phone_number_is_refused_in_the_language_of_the_site(
        string phone, string slug, string itemPath, string cartPath, string message)
    {
        await NewTenantAsync(slug);
        var client = app.CreateClient($"{slug}.test");
        await AddAsync(client, $"{itemPath}?van={TestReservations.FutureDay(104):yyyy-MM-dd}");
        await IdentifyAsync(client, "zonder-telefoon@example.test", cartPath: cartPath);

        var response = await ReserveAsync(client, "Zoë", cartPath, phone: phone);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html);
        Assert.Contains("value=\"Zoë\"", html);
        await using var scope = await app.TenantScopeAsync(slug);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AnyAsync());
    }

    [Fact]
    public async Task A_Belgian_phone_number_from_the_cart_is_kept_with_the_country_code()
    {
        await NewTenantAsync("kar-tel-be");
        var client = app.CreateClient("kar-tel-be.test");

        await ReserveItemAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(105):yyyy-MM-dd}", "belg@example.test", "Bram", phone: "0470/12.34.56");

        await using var scope = await app.TenantScopeAsync("kar-tel-be");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.Include(r => r.Customer).SingleAsync();
        Assert.Equal("+32470123456", reservation.Customer!.Phone);
    }

    [Fact]
    public async Task Clicking_reserve_twice_at_once_makes_one_reservation_and_shows_it_both_times()
    {
        await NewTenantAsync("kar-dubbelklik", stock: 5);
        var client = app.CreateClient("kar-dubbelklik.test");
        var day = TestReservations.FutureDay(101);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        await IdentifyAsync(client, "dubbel@example.test");

        // Twee keer hetzelfde formulier tegelijk versturen, zoals bij een dubbelklik.
        var page = await PageAsync(client, "/winkelkar");
        Dictionary<string, string> Fields() => new()
        {
            ["_handler"] = "reservation",
            ["Form.FirstName"] = "Dubbel",
            ["Form.LastName"] = "Klant",
            ["Form.Phone"] = "0470 12 34 56",
            ["Form.Street"] = "Kerkstraat 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Pickup",
            ["Form.SameBillingAddress"] = "True",
        };
        var responses = await Task.WhenAll(PostAsync(client, "/winkelkar", page, Fields()), PostAsync(client, "/winkelkar", page, Fields()));

        await using var scope = await app.TenantScopeAsync("kar-dubbelklik");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.SingleAsync();
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            Assert.Contains($"Je reservatie {reservation.Number} is bevestigd.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        Assert.Single(app.Services.GetRequiredService<RecordingMailTransport>().Sent, m => m.To == "dubbel@example.test");
    }

    [Fact]
    public async Task Without_pickup_the_customer_gets_no_choice_and_the_rental_company_always_delivers()
    {
        await NewTenantAsync("kar-leveren");
        var client = app.CreateClient("kar-leveren.test");
        var day = TestReservations.FutureDay(102);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        await IdentifyAsync(client, "leveren@example.test");

        Assert.DoesNotContain("Form.DeliveryMethod", await PageAsync(client, "/winkelkar"));
        // Ook wie toch "afhalen" meestuurt, krijgt een levering.
        (await ReserveAsync(client, "Leo", delivery: "Pickup")).EnsureSuccessStatusCode();

        await using var scope = await app.TenantScopeAsync("kar-leveren");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.SingleAsync();
        Assert.Equal(DeliveryMethod.Delivery, reservation.DeliveryMethod);
        Assert.Equal("Kerkstraat 1", reservation.DeliveryAddress!.Street);
    }

    [Fact]
    public async Task With_pickup_the_customer_chooses_to_pick_up()
    {
        await NewTenantAsync("kar-afhalen");
        await using (var setup = await app.TenantScopeAsync("kar-afhalen"))
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Slug == "kar-afhalen");
            tenant.OffersPickup = true;
            await db.SaveChangesAsync();
            setup.ServiceProvider.GetRequiredService<Moshtar.Infrastructure.Tenancy.ITenantStore>().Forget(tenant.Id);
        }
        var client = app.CreateClient("kar-afhalen.test");
        var day = TestReservations.FutureDay(103);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        await IdentifyAsync(client, "afhalen@example.test");

        Assert.Contains("Zelf afhalen", await PageAsync(client, "/winkelkar"));
        (await ReserveAsync(client, "Ann", delivery: "Pickup")).EnsureSuccessStatusCode();

        await using var scope = await app.TenantScopeAsync("kar-afhalen");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.SingleAsync();
        Assert.Equal(DeliveryMethod.Pickup, reservation.DeliveryMethod);
    }

    [Fact]
    public async Task Step_one_keeps_the_email_and_both_choices_which_are_off_by_default()
    {
        await NewTenantAsync("kar-vinkjes");
        var client = app.CreateClient("kar-vinkjes.test");
        await AddAsync(client, $"/fr/louer/prinses?van={TestReservations.FutureDay(110):yyyy-MM-dd}");

        var page = await PageAsync(client, "/fr/panier");
        Assert.Contains("<input type=\"checkbox\" name=\"herinnering\" value=\"ja\" />", page);
        Assert.Contains("<input type=\"checkbox\" name=\"nieuws\" value=\"ja\" />", page);
        Assert.Contains("Je souhaite recevoir les nouveautés et promotions de kar-vinkjes", page);
        await IdentifyAsync(client, "louis@example.test", reminder: true, news: false, cartPath: "/fr/panier");

        var cart = await CartAsync("kar-vinkjes");
        Assert.Equal(("louis@example.test", "fr", true, false), (cart.Email, cart.Culture, cart.WantsReminder, cart.WantsNews));
        // Het e-mailadres wijzigen toont stap 1 opnieuw, met wat al gekozen was.
        var change = await PageAsync(client, "/fr/panier?stap=email");
        Assert.Contains("value=\"louis@example.test\"", change);
        Assert.Contains("<input type=\"checkbox\" name=\"herinnering\" value=\"ja\" checked />", change);
    }

    [Fact]
    public async Task An_invalid_email_gives_a_message_and_is_not_kept()
    {
        await NewTenantAsync("kar-email-fout");
        var client = app.CreateClient("kar-email-fout.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(120):yyyy-MM-dd}");

        var response = await IdentifyAsync(client, "geen-adres");

        Assert.Equal("/winkelkar?stap=email&fout=EmailInvalid#reserveren", response.Headers.Location!.OriginalString);
        Assert.Contains("Dat e-mailadres klopt niet.", await PageAsync(client, "/winkelkar?stap=email&fout=EmailInvalid"));
        Assert.Null((await CartAsync("kar-email-fout")).Email);
    }

    [Fact]
    public async Task When_something_was_just_taken_the_customer_sees_what_and_keeps_his_details()
    {
        var tenant = await NewTenantAsync("kar-net-weg");
        var client = app.CreateClient("kar-net-weg.test");
        var day = TestReservations.FutureDay(130);
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}");
        await AddAsync(client, $"/huren/piraat?van={day:yyyy-MM-dd}");
        await IdentifyAsync(client, "tom@example.test");
        var page = await PageAsync(client, "/winkelkar");

        // Iemand anders reserveert intussen het piratenkasteel.
        await ReserveForSomeoneElseAsync("kar-net-weg", tenant.Pirate, day);
        var response = await PostAsync(client, "/winkelkar", page, new()
        {
            ["_handler"] = "reservation",
            ["Form.FirstName"] = "Tom",
            ["Form.LastName"] = "Laat",
            ["Form.Phone"] = "0470 12 34 56",
            ["Form.Street"] = "Kerkstraat 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Pickup",
            ["Form.SameBillingAddress"] = "True",
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Net niet meer vrij op deze datum: Piratenkasteel.", html);
        Assert.Contains("<span class=\"badge badge-busy\">Niet vrij op deze datum</span>", html);
        Assert.Contains("value=\"Laat\"", html);
        await using var scope = await app.TenantScopeAsync("kar-net-weg");
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AnyAsync(r => r.Customer!.Email == "tom@example.test"));
    }

    [Fact]
    public async Task Step_two_offers_private_or_business_and_the_same_billing_address_by_default()
    {
        await NewTenantAsync("kar-klanttype");
        var client = app.CreateClient("kar-klanttype.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(150):yyyy-MM-dd}");
        await IdentifyAsync(client, "type@example.test");

        var html = await PageAsync(client, "/winkelkar");

        Assert.Contains("Particulier", html);
        Assert.Contains("Bedrijf of organisatie", html);
        Assert.Matches("<input[^>]*name=\"Form.Kind\"[^>]*value=\"Private\"[^>]*checked", html);
        Assert.Matches("<input[^>]*type=\"checkbox\"[^>]*name=\"Form.SameBillingAddress\"[^>]*checked", html);
        Assert.Contains("Factuuradres is hetzelfde", html);
        Assert.DoesNotContain("name=\"Form.CompanyName\"", html);
        Assert.DoesNotContain("name=\"Form.BillingStreet\"", html);
        Assert.DoesNotContain("<script", html[html.IndexOf("id=\"reserveren\"", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task Choosing_business_and_another_billing_address_shows_their_fields_and_keeps_what_was_typed()
    {
        await NewTenantAsync("kar-velden");
        var client = app.CreateClient("kar-velden.test");
        await AddAsync(client, $"/fr/louer/prinses?van={TestReservations.FutureDay(151):yyyy-MM-dd}");
        await IdentifyAsync(client, "velden@example.test", cartPath: "/fr/panier");
        var page = await PageAsync(client, "/fr/panier");

        // Zonder JavaScript: de klant kiest en drukt op "Mettre à jour"; het formulier komt terug met de juiste velden.
        var response = await PostAsync(client, "/fr/panier", page, new()
        {
            ["_handler"] = "reservation",
            ["Form.Kind"] = "Business",
            ["Form.FirstName"] = "Élise",
            ["Form.Refresh"] = "ja",
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Form.CompanyName\"", html);
        Assert.Contains("Numéro de TVA (facultatif)", html);
        Assert.Contains("name=\"Form.BillingStreet\"", html);
        Assert.Contains("Adresse de facturation", html);
        Assert.Matches("<input[^>]*name=\"Form.Kind\"[^>]*value=\"Business\"[^>]*checked", html);
        Assert.Contains("value=\"Élise\"", html);
        Assert.DoesNotContain("validation-message", html);
        Assert.DoesNotContain("validation-errors", html);
        await using var scope = await app.TenantScopeAsync("kar-velden");
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AnyAsync());
    }

    [Fact]
    public async Task A_business_reserves_with_company_vat_number_and_billing_address_and_becomes_a_business_customer()
    {
        await NewTenantAsync("kar-zakelijk");
        var client = app.CreateClient("kar-zakelijk.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(152):yyyy-MM-dd}");
        await IdentifyAsync(client, "juf.els@school.test");

        var response = await ReserveAsync(client, "Els", extra: new()
        {
            ["Form.Kind"] = "Business",
            ["Form.CompanyName"] = "Basisschool De Linde",
            ["Form.VatNumber"] = "BE 0123.456.789",
            ["Form.SameBillingAddress"] = null,
            ["Form.BillingStreet"] = "Schoolstraat 3",
            ["Form.BillingPostalCode"] = "9050",
            ["Form.BillingCity"] = "Gentbrugge",
        });

        response.EnsureSuccessStatusCode();
        Assert.Contains("is bevestigd", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await using var scope = await app.TenantScopeAsync("kar-zakelijk");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var card = (await customers.GetAsync(Assert.Single((await customers.SearchAsync(new CustomerSearch())).Items).Id))!;
        var customer = card.Customer;
        Assert.Equal((CustomerKind.Business, "Basisschool De Linde", "BE0123456789"), (customer.Kind, customer.CompanyName, customer.VatNumber));
        Assert.Equal(("Els", "Antwerpen", "Gentbrugge"), (customer.FirstName, customer.Address.City, customer.BillingAddressOrAddress.City));
        Assert.Equal("Kerkstraat 1", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.SingleAsync()).DeliveryAddress!.Street);
        // De nagebootste btw-databank is onbereikbaar voor dit nummer: aanvaard, later opnieuw gecontroleerd.
        Assert.Equal(VatCheckStatus.NotChecked, customer.VatCheck);
    }

    [Fact]
    public async Task A_valid_vat_number_keeps_what_the_customer_typed_and_stores_the_official_name_and_address_apart()
    {
        app.VatRegistry.Valid("BE0200000001", "VZW GEMEENTELIJKE BASISSCHOOL", "SCHOOLSTRAAT 3\n9050 GENTBRUGGE");
        await NewTenantAsync("kar-btw-geldig");
        var client = app.CreateClient("kar-btw-geldig.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(156):yyyy-MM-dd}");
        await IdentifyAsync(client, "btw-geldig@school.test");

        var response = await ReserveAsync(client, "Els", extra: new()
        {
            ["Form.Kind"] = "Business", ["Form.CompanyName"] = "Basisschool De Linde", ["Form.VatNumber"] = "BE0200000001",
        });

        Assert.Contains("is bevestigd", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await using var scope = await app.TenantScopeAsync("kar-btw-geldig");
        var customer = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers.AsNoTracking().SingleAsync();
        Assert.Equal((VatCheckStatus.Valid, "Basisschool De Linde", null), (customer.VatCheck, customer.CompanyName, customer.BillingAddress));
        Assert.Equal(("VZW GEMEENTELIJKE BASISSCHOOL", "SCHOOLSTRAAT 3\n9050 GENTBRUGGE"), (customer.VatRegistryName, customer.VatRegistryAddress));
        Assert.NotNull(customer.VatCheckedAtUtc);
    }

    [Theory]
    [InlineData("kar-btw-ongeldig-nl", "/huren/prinses", "/winkelkar", "BE0200000002", "Dit btw-nummer is ongeldig volgens de btw-databank van de EU. Verbeter het of laat het leeg.")]
    [InlineData("kar-btw-ongeldig-fr", "/fr/louer/prinses", "/fr/panier", "BE0200000003", "Ce numéro de TVA n'est pas valide selon la base de données TVA de l'UE. Corrigez-le ou laissez-le vide.")]
    [InlineData("kar-btw-ongeldig-en", "/en/rent/prinses", "/en/cart", "BE0200000004", "This VAT number is not valid according to the EU VAT database. Correct it or leave it empty.")]
    public async Task An_invalid_vat_number_is_refused_in_the_language_of_the_site(string slug, string itemPath, string cartPath, string vat, string message)
    {
        app.VatRegistry.Answer(vat, VatRegistryAnswer.Invalid);
        await NewTenantAsync(slug);
        var client = app.CreateClient($"{slug}.test");
        await AddAsync(client, $"{itemPath}?van={TestReservations.FutureDay(157):yyyy-MM-dd}");
        await IdentifyAsync(client, "btw-ongeldig@example.test", cartPath: cartPath);

        var response = await ReserveAsync(client, "Zoë", cartPath, extra: new()
        {
            ["Form.Kind"] = "Business", ["Form.CompanyName"] = "Chiro", ["Form.VatNumber"] = vat,
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains(message, html);
        Assert.Contains($"value=\"{vat}\"", html);
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Reservations.AnyAsync());
        Assert.False(await db.Customers.AnyAsync());
    }

    [Fact]
    public async Task A_vat_number_from_outside_the_eu_is_accepted_as_not_checkable()
    {
        await NewTenantAsync("kar-btw-buiten-eu");
        var client = app.CreateClient("kar-btw-buiten-eu.test");
        await AddAsync(client, $"/huren/prinses?van={TestReservations.FutureDay(158):yyyy-MM-dd}");
        await IdentifyAsync(client, "btw-buiten-eu@example.test");

        var response = await ReserveAsync(client, "Urs", extra: new()
        {
            ["Form.Kind"] = "Business", ["Form.CompanyName"] = "Hüpfburg AG", ["Form.VatNumber"] = "CHE-123.456.788",
        });

        Assert.Contains("is bevestigd", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await using var scope = await app.TenantScopeAsync("kar-btw-buiten-eu");
        var customer = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers.AsNoTracking().SingleAsync();
        Assert.Equal(("CHE123456788", VatCheckStatus.NotCheckable), (customer.VatNumber, customer.VatCheck));
        Assert.Equal(0, app.VatRegistry.LookupsOf("CHE123456788"));
    }

    [Theory]
    [InlineData("kar-bedrijf-nl", "/huren/prinses", "/winkelkar", "", "", "True", "Vul de naam van je bedrijf of organisatie in.")]
    [InlineData("kar-btw-nl", "/huren/prinses", "/winkelkar", "Chiro", "BE 123.456.789", "True", "Dit is geen geldig btw-nummer. Begin met de landcode, zoals BE0123456789, of laat het leeg.")]
    [InlineData("kar-btw-fr", "/fr/louer/prinses", "/fr/panier", "Patro", "BE12", "True", "Ce n'est pas un numéro de TVA valide. Commencez par le code pays, comme BE0123456789, ou laissez-le vide.")]
    [InlineData("kar-factuur-en", "/en/rent/prinses", "/en/cart", "Scouts", "", null, "Please enter the street and number, postal code and city of your billing address.")]
    public async Task Incomplete_business_details_are_refused_in_the_language_of_the_site(
        string slug, string itemPath, string cartPath, string company, string vat, string? sameBilling, string message)
    {
        await NewTenantAsync(slug);
        var client = app.CreateClient($"{slug}.test");
        await AddAsync(client, $"{itemPath}?van={TestReservations.FutureDay(153):yyyy-MM-dd}");
        await IdentifyAsync(client, "onvolledig@example.test", cartPath: cartPath);

        var response = await ReserveAsync(client, "Zoë", cartPath, extra: new()
        {
            ["Form.Kind"] = "Business", ["Form.CompanyName"] = company, ["Form.VatNumber"] = vat, ["Form.SameBillingAddress"] = sameBilling,
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html);
        Assert.Contains("value=\"Zoë\"", html);
        await using var scope = await app.TenantScopeAsync(slug);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AnyAsync());
    }

    [Fact]
    public async Task A_cart_alone_makes_no_customer_but_a_cancelled_reservation_keeps_its_customer()
    {
        await NewTenantAsync("kar-geen-klant");
        var onlyCart = app.CreateClient("kar-geen-klant.test");
        await AddAsync(onlyCart, $"/huren/prinses?van={TestReservations.FutureDay(154):yyyy-MM-dd}");
        await IdentifyAsync(onlyCart, "alleen-winkelkar@example.test", news: true);
        await ReserveItemAsync(app.CreateClient("kar-geen-klant.test"), $"/huren/piraat?van={TestReservations.FutureDay(155):yyyy-MM-dd}",
            "geannuleerd@example.test", "Gina");

        await using var scope = await app.TenantScopeAsync("kar-geen-klant");
        var reservation = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.SingleAsync();
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(reservation.Id, ReservationStatus.Cancelled, ReservationActor.Website);
        var customers = await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().SearchAsync(new CustomerSearch());

        Assert.Equal(["geannuleerd@example.test"], customers.Items.Select(c => c.Email));
    }

    [Fact]
    public async Task The_item_page_no_longer_has_the_reservation_form()
    {
        await NewTenantAsync("kar-geen-formulier");

        var html = await PageAsync(app.CreateClient("kar-geen-formulier.test"), $"/huren/prinses?van={TestReservations.FutureDay(140):yyyy-MM-dd}");

        Assert.Contains("In winkelkar</button>", html);
        Assert.DoesNotContain("name=\"Form.FirstName\"", html);
        Assert.DoesNotContain("Reservatie bevestigen", html);
    }

    private static readonly System.Globalization.CultureInfo Dutch = System.Globalization.CultureInfo.GetCultureInfo("nl");

    private sealed record TestTenant(Guid Princess, Guid Pirate, Guid Mill, Guid Party);

    /// <summary>Een verhuurder met springkastelen (prinses en piraat), een molen en een feestpakket met de molen.</summary>
    private async Task<TestTenant> NewTenantAsync(string slug, int stock = 1)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var princess = new RentalItem { Slug = "prinses", Stock = stock, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 }, Translations = [T("nl", "Prinsessenkasteel")] };
        var pirate = new RentalItem { Slug = "piraat", Stock = 1, Pricing = new Pricing { DayPrice = 100 }, Translations = [T("nl", "Piratenkasteel")] };
        var mill = new RentalItem { Slug = "molen", Stock = 1, Pricing = new Pricing { DayPrice = 30 }, Translations = [T("nl", "Molen")] };
        db.AddRange(princess, pirate, mill);
        await db.SaveChangesAsync();
        var party = new Bundle { Slug = "feest", Pricing = new Pricing { DayPrice = 150 }, Translations = [T("nl", "Feestpakket")], Items = [new BundleItem { RentalItemId = mill.Id }] };
        db.Bundles.Add(party);
        await db.SaveChangesAsync();
        return new TestTenant(princess.Id, pirate.Id, mill.Id, party.Id);
    }

    private async Task<Moshtar.Domain.Carts.Cart> CartAsync(string tenantSlug)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Carts.AsNoTracking().SingleAsync();
    }

    private async Task ReserveForSomeoneElseAsync(string tenantSlug, Guid itemId, DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day),
            [new ReservationLineRequest(itemId, null, 1)],
            new ReservationCustomer("Test", "Klant", "klant@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, "nl"), ReservationActor.Website);
        Assert.True(result.Succeeded);
    }

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
