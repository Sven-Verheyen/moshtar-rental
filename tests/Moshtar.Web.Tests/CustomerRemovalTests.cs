using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Application.Marketing;
using Moshtar.Application.Reservations;
using Moshtar.Application.Reviews;
using Moshtar.Application.Schedule;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Marketing;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De beheerder verwijdert een klant zonder reservaties, of anonimiseert een klant met reservaties: zijn
/// persoonsgegevens worden gewist, zijn reservaties blijven zonder te tonen van wie ze waren.
/// </summary>
public sealed class CustomerRemovalTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Gent = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };

    private static CustomerDetails Details(string email) =>
        new(CustomerKind.Private, "Els", "Peeters", email, "0470 12 34 56", Gent, "nl");

    [Fact]
    public async Task The_administrator_deletes_a_customer_without_reservations()
    {
        var admin = await app.CreateUserAsync("hopsakee", "verwijder-beheerder@hopsakee.test", UserRole.Administrator);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = (await customers.AddAsync(Details("verwijder-mij@example.test"), admin.Id)).CustomerId!.Value;

        var result = await customers.DeleteAsync(id, admin.Id);

        Assert.True(result.Succeeded);
        Assert.Null(await customers.GetAsync(id));
        Assert.Empty((await customers.SearchAsync(new CustomerSearch("verwijder-mij@example.test"))).Items);
    }

    [Fact]
    public async Task A_customer_with_reservations_cannot_be_deleted_even_if_they_were_cancelled()
    {
        var admin = await app.CreateUserAsync("hopsakee", "verwijder-reservatie@hopsakee.test", UserRole.Administrator);
        var reservation = await ReserveAsync("heeft-gereserveerd@example.test", TestReservations.FutureDay(-301));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ChangeStatusAsync(reservation, ReservationStatus.Cancelled, ReservationActor.User(admin.Id));
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = await FindAsync(customers, "heeft-gereserveerd@example.test");

        var result = await customers.DeleteAsync(id, admin.Id);

        Assert.Equal(["Deze klant heeft reservaties en kan niet verwijderd worden. Anonimiseer hem in de plaats."], result.Errors);
        Assert.NotNull(await customers.GetAsync(id));
    }

    [Fact]
    public async Task Staff_can_neither_delete_nor_anonymize_a_customer()
    {
        var staff = await app.CreateUserAsync("hopsakee", "verwijder-medewerker@hopsakee.test", UserRole.Staff);
        await ReserveAsync("medewerker-anoniem@example.test", TestReservations.FutureDay(-302));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var withoutReservations = (await customers.AddAsync(Details("medewerker-verwijder@example.test"), staff.Id)).CustomerId!.Value;
        var withReservations = await FindAsync(customers, "medewerker-anoniem@example.test");

        var deleted = await customers.DeleteAsync(withoutReservations, staff.Id);
        var anonymized = await customers.AnonymizeAsync(withReservations, staff.Id);

        Assert.Equal(["Alleen de beheerder kan klanten verwijderen of anonimiseren."], deleted.Errors);
        Assert.Equal(["Alleen de beheerder kan klanten verwijderen of anonimiseren."], anonymized.Errors);
        Assert.Equal("medewerker-verwijder@example.test", (await customers.GetAsync(withoutReservations))!.Customer.Email);
        Assert.Equal("medewerker-anoniem@example.test", (await customers.GetAsync(withReservations))!.Customer.Email);
    }

    [Fact]
    public async Task Anonymizing_wipes_the_personal_details_and_keeps_the_reservations_without_showing_whose_they_were()
    {
        var admin = await app.CreateUserAsync("hopsakee", "anoniem-beheerder@hopsakee.test", UserRole.Administrator);
        var day = TestReservations.FutureDay(-303);
        var reservation = await ReserveAsync("anja.anoniem@example.test", day);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ChangeStatusAsync(reservation, ReservationStatus.Completed, ReservationActor.User(admin.Id));
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = await FindAsync(customers, "anja.anoniem@example.test");
        await customers.UpdateAsync(id, Details("anja.anoniem@example.test") with
        {
            Kind = CustomerKind.Business, CompanyName = "Anja's Feestjes", VatNumber = "BE0123456789",
            BillingAddress = new Address { Street = "Factuurlaan 9", PostalCode = "9050", City = "Gentbrugge" }, Notes = "Vaste klant",
        }, admin.Id);

        var result = await customers.AnonymizeAsync(id, admin.Id);

        Assert.True(result.Succeeded);
        var card = (await customers.GetAsync(id))!;
        var c = card.Customer;
        Assert.True(c.IsAnonymized);
        Assert.Equal(("", "", null, null, null, null), (c.FirstName, c.LastName, c.CompanyName, c.VatNumber, c.Phone, c.Notes));
        Assert.DoesNotContain("anja", c.Email);
        Assert.Equal(("", "", "", null), (c.Address.Street, c.Address.PostalCode, c.Address.City, c.BillingAddress));
        Assert.Equal(reservation, Assert.Single(card.Reservations).Id);
        Assert.Equal("anoniem-beheerder@hopsakee.test", card.UpdatedBy);

        var stop = Assert.Single((await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day)).Deliveries,
            s => s.ReservationId == reservation);
        Assert.Equal(("", null, null), (stop.CustomerName, stop.Phone, stop.Notes));
        Assert.Equal(("", "9000", "Gent"), (stop.Address!.Street, stop.Address.PostalCode, stop.Address.City));
        Assert.Empty((await customers.SearchAsync(new CustomerSearch("anja.anoniem"))).Items);
    }

    [Fact]
    public async Task Anonymizing_removes_the_marketing_contact_but_keeps_the_review_opt_out()
    {
        var admin = await app.CreateUserAsync("hopsakee", "anoniem-marketing@hopsakee.test", UserRole.Administrator);
        var reservation = await ReserveAsync("marketing.anoniem@example.test", TestReservations.FutureDay(-304));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ChangeStatusAsync(reservation, ReservationStatus.Cancelled, ReservationActor.User(admin.Id));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.MarketingContacts.Add(new MarketingContact
        {
            Email = "marketing.anoniem@example.test", Culture = "nl", ConsentedAtUtc = DateTime.UtcNow, Source = MarketingContactSource.Reservation,
        });
        await db.SaveChangesAsync();
        var optOuts = scope.ServiceProvider.GetRequiredService<IReviewOptOuts>();
        await optOuts.OptOutAsync("marketing.anoniem@example.test");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        Assert.True((await customers.AnonymizeAsync(await FindAsync(customers, "marketing.anoniem@example.test"), admin.Id)).Succeeded);

        var marketing = await scope.ServiceProvider.GetRequiredService<IMarketingContactAdministration>().ListAsync();
        Assert.DoesNotContain(marketing, m => m.Email == "marketing.anoniem@example.test");
        Assert.True(await optOuts.IsOptedOutAsync("marketing.anoniem@example.test"));
    }

    [Fact]
    public async Task An_anonymized_customer_does_not_block_a_new_customer_with_the_same_email_address()
    {
        var admin = await app.CreateUserAsync("hopsakee", "anoniem-opnieuw@hopsakee.test", UserRole.Administrator);
        var first = await ReserveAsync("terug@example.test", TestReservations.FutureDay(-305));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ChangeStatusAsync(first, ReservationStatus.Completed, ReservationActor.User(admin.Id));
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var anonymized = await FindAsync(customers, "terug@example.test");
        await customers.AnonymizeAsync(anonymized, admin.Id);

        await ReserveAsync("terug@example.test", TestReservations.FutureDay(-306), firstName: "Terug");

        var again = (await customers.GetAsync(await FindAsync(customers, "terug@example.test")))!;
        Assert.NotEqual(anonymized, again.Customer.Id);
        Assert.Equal("Terug", again.Customer.FirstName);
        Assert.Single(again.Reservations);
        Assert.Single((await customers.GetAsync(anonymized))!.Reservations);
    }

    [Fact]
    public async Task A_customer_with_a_running_reservation_or_without_reservations_is_not_anonymized()
    {
        var admin = await app.CreateUserAsync("hopsakee", "anoniem-lopend@hopsakee.test", UserRole.Administrator);
        await ReserveAsync("lopend@example.test", TestReservations.FutureDay(-307));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var running = await FindAsync(customers, "lopend@example.test");
        var none = (await customers.AddAsync(Details("zonder-reservaties@example.test"), admin.Id)).CustomerId!.Value;

        var refusedRunning = await customers.AnonymizeAsync(running, admin.Id);
        var refusedNone = await customers.AnonymizeAsync(none, admin.Id);

        Assert.Equal(["Deze klant heeft nog een lopende reservatie. Annuleer ze of rond ze af voor je hem anonimiseert."], refusedRunning.Errors);
        Assert.Equal(["Deze klant heeft geen reservaties. Verwijder hem in de plaats."], refusedNone.Errors);
        Assert.Equal("lopend@example.test", (await customers.GetAsync(running))!.Customer.Email);
    }

    [Fact]
    public async Task An_anonymized_customer_cannot_be_changed_or_anonymized_again()
    {
        var admin = await app.CreateUserAsync("hopsakee", "anoniem-wijzig@hopsakee.test", UserRole.Administrator);
        var reservation = await ReserveAsync("wijzig-anoniem@example.test", TestReservations.FutureDay(-308));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>()
            .ChangeStatusAsync(reservation, ReservationStatus.Cancelled, ReservationActor.User(admin.Id));
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = await FindAsync(customers, "wijzig-anoniem@example.test");
        await customers.AnonymizeAsync(id, admin.Id);

        var changed = await customers.UpdateAsync(id, Details("wijzig-anoniem@example.test"), admin.Id);
        var again = await customers.AnonymizeAsync(id, admin.Id);

        Assert.Equal(["Deze klant is geanonimiseerd en kan niet meer gewijzigd worden."], changed.Errors);
        Assert.Equal(["Deze klant is al geanonimiseerd."], again.Errors);
        Assert.Equal("", (await customers.GetAsync(id))!.Customer.FirstName);
        // Zijn oude e-mailadres is weer vrij voor een nieuwe klant.
        Assert.True((await customers.AddAsync(Details("wijzig-anoniem@example.test"), admin.Id)).Succeeded);
    }

    [Fact]
    public async Task An_anonymized_customer_gets_no_review_request()
    {
        await ReviewTenant.CreateAsync(app, "anoniem-review");
        var admin = await app.CreateUserAsync("anoniem-review", "beheerder@anoniem-review.test", UserRole.Administrator);
        var end = TestReservations.FutureDay(41);
        var reservation = await ReviewTenant.ReserveAsync(app, "anoniem-review", "review-anoniem@example.test", "Rita", end);
        await using (var scope = await app.TenantScopeAsync("anoniem-review"))
        {
            await scope.ServiceProvider.GetRequiredService<IReservationService>()
                .ChangeStatusAsync(reservation, ReservationStatus.Completed, ReservationActor.User(admin.Id));
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            Assert.True((await customers.AnonymizeAsync(await FindAsync(customers, "review-anoniem@example.test"), admin.Id)).Succeeded);
        }

        Assert.Equal(0, await ReviewTenant.RunAsync(app, "anoniem-review", ReviewTenant.At(end.AddDays(1), 10, 0)));
    }

    [Fact]
    public async Task Only_the_administrator_sees_the_buttons_to_delete_or_anonymize_on_the_customer_card()
    {
        var adminClient = await app.LoggedInClientAsync("knoppen-beheerder@hopsakee.test", UserRole.Administrator);
        var staffClient = await app.LoggedInClientAsync("knoppen-medewerker@hopsakee.test", UserRole.Staff);
        var admin = await app.CreateUserAsync("hopsakee", "knoppen-beheerder@hopsakee.test", UserRole.Administrator);
        await ReserveAsync("knoppen-reservatie@example.test", TestReservations.FutureDay(-309));
        Guid withReservations, withoutReservations;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            withReservations = await FindAsync(customers, "knoppen-reservatie@example.test");
            withoutReservations = (await customers.AddAsync(Details("knoppen-zonder@example.test"), admin.Id)).CustomerId!.Value;
        }

        async Task<string> Card(HttpClient client, Guid id) => WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/klanten/{id}"));

        Assert.Contains("Anonimiseren", await Card(adminClient, withReservations));
        Assert.DoesNotContain("Klant verwijderen", await Card(adminClient, withReservations));
        Assert.Contains("Klant verwijderen", await Card(adminClient, withoutReservations));
        Assert.DoesNotContain("Anonimiseren", await Card(adminClient, withoutReservations));
        Assert.DoesNotContain("Anonimiseren", await Card(staffClient, withReservations));
        Assert.DoesNotContain("Klant verwijderen", await Card(staffClient, withoutReservations));
    }

    [Fact]
    public async Task The_card_of_an_anonymized_customer_says_so_and_cannot_be_changed()
    {
        var client = await app.LoggedInClientAsync("fiche-anoniem@hopsakee.test", UserRole.Administrator);
        var admin = await app.CreateUserAsync("hopsakee", "fiche-anoniem@hopsakee.test", UserRole.Administrator);
        var reservation = await ReserveAsync("fiche-anoniem@example.test", TestReservations.FutureDay(-310), firstName: "Fientje");
        Guid id;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            await scope.ServiceProvider.GetRequiredService<IReservationService>()
                .ChangeStatusAsync(reservation, ReservationStatus.Completed, ReservationActor.User(admin.Id));
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            id = await FindAsync(customers, "fiche-anoniem@example.test");
            await customers.AnonymizeAsync(id, admin.Id);
        }

        var card = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/klanten/{id}"));

        Assert.Contains("Geanonimiseerde klant", card);
        Assert.DoesNotContain("Fientje", card);
        Assert.DoesNotContain("Wijzigen", card);
        Assert.DoesNotContain("Anonimiseren", card);
    }

    [Theory]
    [InlineData("/privacy", "<h2>Klantenbestand</h2>", "in zijn klantenbestand", "btw-databank van de Europese Unie", "om je gegevens te wissen")]
    [InlineData("/fr/confidentialite", "<h2>Fichier clients</h2>", "dans son fichier clients", "base de données TVA de l'Union européenne", "l'effacement de vos données")]
    [InlineData("/en/privacy", "<h2>Customer records</h2>", "in its customer records", "VAT database of the European Union", "to erase your data")]
    public async Task The_privacy_statement_explains_the_customer_records_the_vat_check_and_the_right_to_erasure(
        string path, string heading, string records, string vatDatabase, string erasure)
    {
        var page = WebUtility.HtmlDecode(await app.CreateClient(MoshtarApp.HopsakeeHost).GetStringAsync(path));

        Assert.Contains(heading, page);
        var paragraph = page[page.IndexOf(heading)..];
        Assert.Contains(records, paragraph);
        Assert.Contains(vatDatabase, paragraph);
        Assert.Contains(erasure, paragraph);
    }

    private static async Task<Guid> FindAsync(ICustomerAdministration customers, string email) =>
        Assert.Single((await customers.SearchAsync(new CustomerSearch(email))).Items).Id;

    /// <summary>Een reservatie in het back office, met levering en een opmerking.</summary>
    private async Task<Guid> ReserveAsync(string email, DateOnly day, string firstName = "Anja")
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new RentalItem
        {
            Slug = $"anoniem-{Guid.NewGuid():N}", Stock = 5, Pricing = new Pricing { DayPrice = 100 },
            Translations = [new Translation { Culture = "nl", Name = "Anoniemkasteel" }],
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync();
        var admin = await app.CreateUserAsync("hopsakee", "reserveer-beheerder@hopsakee.test", UserRole.Administrator);
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day), [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, "Janssens", email, "0470 12 34 56", Gent),
            DeliveryMethod.Delivery, new Address { Street = "Kerkstraat 7", PostalCode = "9000", City = "Gent" },
            "Bel aan bij Anja", "nl"), ReservationActor.User(admin.Id));
        Assert.NotNull(result.Reservation);
        return result.Reservation.Id;
    }
}
