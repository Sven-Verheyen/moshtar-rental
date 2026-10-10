using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>Na een geslaagde reservatie krijgt de klant een bevestiging in zijn eigen taal.</summary>
public sealed class ReservationConfirmationMailTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Reservation_on_the_website_sends_one_confirmation_from_the_rental_company()
    {
        await CreateItemAsync("hopsakee", "mail-website", stock: 1, name: "Springkasteel Piraat");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        var day = TestReservations.FutureDay(300);

        var form = await client.GetStringAsync("/huren/mail-website");
        var response = await client.PostAsync("/huren/mail-website", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "reservation",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Form.StartDate"] = day.ToString("yyyy-MM-dd"),
            ["Form.EndDate"] = day.ToString("yyyy-MM-dd"),
            ["Form.Quantity"] = "1",
            ["Form.FirstName"] = "Lotte",
            ["Form.LastName"] = "Website",
            ["Form.Email"] = "lotte@example.test",
            ["Form.Street"] = "Kerkstraat 1",
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = "Pickup",
        }));
        response.EnsureSuccessStatusCode();

        var mail = Assert.Single(Outbox.Sent, m => m.To == "lotte@example.test");
        var number = await NumberOfLatestReservationAsync("lotte@example.test");
        Assert.Equal(("noreply@hopsakee.test", "Hopsakee.fun", "info@hopsakee.test"), (mail.FromAddress, mail.FromName, mail.ReplyTo));
        Assert.Equal($"Bevestiging van je reservatie {number} bij Hopsakee.fun", mail.Subject);
        foreach (var body in new[] { mail.TextBody, System.Net.WebUtility.HtmlDecode(mail.HtmlBody) })
        {
            Assert.Contains("Lotte", body);
            Assert.Contains("1 × Springkasteel Piraat", body);
            Assert.Contains(day.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("nl-BE")), body);
            Assert.Contains("Afhalen", body);
            Assert.Contains("€ 100,00", body);
        }
    }

    [Fact]
    public async Task Reservation_entered_in_the_back_office_also_sends_a_confirmation_with_the_delivery_address()
    {
        var user = await app.CreateUserAsync("hopsakee", "mail-invoer@hopsakee.test", UserRole.Staff);
        var item = await CreateItemAsync("hopsakee", "mail-backoffice", stock: 1, name: "Ballenbad");
        var bundle = await CreateBundleAsync("hopsakee", "mail-pakket", name: "Kinderfeest XL");

        var result = await ReserveAsync("hopsakee", "jan@example.test", "nl", ReservationActor.User(user.Id), DeliveryMethod.Delivery,
            new ReservationLineRequest(item, null, 1), new ReservationLineRequest(null, bundle, 2));

        Assert.True(result.Succeeded);
        var mail = Assert.Single(Outbox.Sent, m => m.To == "jan@example.test");
        Assert.Contains(result.Reservation!.Number, mail.Subject);
        Assert.Contains("1 × Ballenbad – € 100,00", mail.TextBody);
        Assert.Contains("2 × Kinderfeest XL – € 300,00", mail.TextBody);
        Assert.Contains("Totaal: € 400,00", mail.TextBody);
        Assert.Contains("Levering op Markt 1, 9000 Gent", mail.TextBody);
    }

    [Theory]
    [InlineData("fr", "fr-BE", "Confirmation de votre réservation", "Retrait", "Total: 100,00 €")]
    [InlineData("en", "en-BE", "Confirmation of your reservation", "Pickup", "Total: €100.00")]
    [InlineData("nl", "nl-BE", "Bevestiging van je reservatie", "Afhalen", "Totaal: € 100,00")]
    public async Task Confirmation_is_in_the_language_of_the_customer(string culture, string cultureName, string subject, string pickup, string total)
    {
        var item = await CreateItemAsync("hopsakee", $"mail-taal-{culture}", stock: 1, name: $"Taal {culture}");

        await ReserveAsync("hopsakee", $"taal-{culture}@example.test", culture, ReservationActor.Website, DeliveryMethod.Pickup,
            new ReservationLineRequest(item, null, 1));

        var mail = Assert.Single(Outbox.Sent, m => m.To == $"taal-{culture}@example.test");
        Assert.StartsWith(subject, mail.Subject);
        Assert.Contains(pickup, mail.TextBody);
        Assert.Contains(total, mail.TextBody);
        Assert.Contains(Day.ToString("d MMMM yyyy", new System.Globalization.CultureInfo(cultureName)), mail.TextBody);
    }

    [Fact]
    public async Task A_refused_reservation_sends_no_mail()
    {
        var item = await CreateItemAsync("hopsakee", "mail-geweigerd", stock: 1, name: "Geweigerd");
        await ReserveAsync("hopsakee", "eerste@example.test", "nl", ReservationActor.Website, DeliveryMethod.Pickup, new ReservationLineRequest(item, null, 1));

        var refused = await ReserveAsync("hopsakee", "tweede@example.test", "nl", ReservationActor.Website, DeliveryMethod.Pickup, new ReservationLineRequest(item, null, 1));

        Assert.False(refused.Succeeded);
        Assert.DoesNotContain(Outbox.Sent, m => m.To == "tweede@example.test");
    }

    [Fact]
    public async Task A_failed_mail_does_not_undo_the_reservation()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant { Name = "Kan niet mailen", Slug = "kan-niet-mailen" });
            await db.SaveChangesAsync();
        }
        var item = await CreateItemAsync("kan-niet-mailen", "mail-mislukt", stock: 1, name: "Mislukt");

        var result = await ReserveAsync("kan-niet-mailen", "geen-mail@example.test", "nl", ReservationActor.Website, DeliveryMethod.Pickup, new ReservationLineRequest(item, null, 1));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(Outbox.Sent, m => m.To == "geen-mail@example.test");
        await using var check = await app.TenantScopeAsync("kan-niet-mailen");
        Assert.True(check.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.Any(r => r.Id == result.Reservation!.Id));
    }

    private static readonly DateOnly Day = TestReservations.FutureDay(310);

    private async Task<ReservationResult> ReserveAsync(string tenant, string email, string culture, ReservationActor actor,
        DeliveryMethod delivery, params ReservationLineRequest[] lines)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        var address = new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" };
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(Day, Day), lines,
            new ReservationCustomer("Klant", "Test", email, null, address),
            delivery, address, null, culture), actor);
    }

    /// <summary>Een pakket van 150 euro per dag met een eigen artikel erin.</summary>
    private async Task<Guid> CreateBundleAsync(string tenant, string slug, string name)
    {
        var content = await CreateItemAsync(tenant, slug + "-inhoud", stock: 5, name: name + " inhoud");
        await using var scope = await app.TenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bundle = new Bundle
        {
            Slug = slug, Pricing = new Pricing { DayPrice = 150 },
            Translations = [new Translation { Culture = "nl", Name = name }],
            Items = [new BundleItem { RentalItemId = content }],
        };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return bundle.Id;
    }

    private async Task<Guid> CreateItemAsync(string tenant, string slug, int stock, string name)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new RentalItem
        {
            Slug = slug, Stock = stock, Pricing = new Pricing { DayPrice = 100 },
            Translations = new[] { "nl", "fr", "en" }.Select(c => new Translation { Culture = c, Name = name }).ToList(),
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<string> NumberOfLatestReservationAsync(string email)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Reservations.Where(r => r.Customer!.Email == email).OrderByDescending(r => r.CreatedAtUtc).Select(r => r.Number).First();
    }
}
