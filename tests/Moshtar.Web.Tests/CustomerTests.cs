using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Application.Reservations;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Marketing;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Het klantenbestand in het back office: klanten zoeken, een klantfiche openen, en klanten manueel toevoegen en
/// wijzigen. Wie reserveert, wordt klant; reserveren werkt de klantfiche bij maar laat de interne notitie staan.
/// </summary>
public sealed class CustomerTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Gent = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };

    private static CustomerDetails Details(string email, string first = "Els", string last = "Peeters", Address? address = null) =>
        new(CustomerKind.Private, first, last, email, "0470 12 34 56", address ?? Gent, "nl");

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task A_user_adds_a_business_customer_with_a_separate_billing_address_and_sees_it_on_the_customer_card(UserRole role)
    {
        var userEmail = $"klant-toevoegen-{role}@hopsakee.test".ToLowerInvariant();
        var user = await app.CreateUserAsync("hopsakee", userEmail, role);
        var billing = new Address { Street = "Schoolstraat 3", PostalCode = "9050", City = "Gentbrugge" };

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var result = await customers.AddAsync(Details($" Juf.Els.{role}@School.test ") with
        {
            Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde", VatNumber = "BE 0123.456.789",
            BillingAddress = billing, Notes = "School, altijd via juf Els",
        }, user.Id);

        var card = Assert.IsType<CustomerCard>(await customers.GetAsync(Assert.IsType<Guid>(result.CustomerId)));
        var customer = card.Customer;
        Assert.Equal($"juf.els.{role}@school.test".ToLowerInvariant(), customer.Email);
        Assert.Equal((CustomerKind.Business, "Basisschool De Linde", "BE0123456789"), (customer.Kind, customer.CompanyName, customer.VatNumber));
        Assert.Equal(("+32470123456", "Gentbrugge", "Gent"), (customer.Phone, customer.BillingAddressOrAddress.City, customer.Address.City));
        Assert.Equal("School, altijd via juf Els", customer.Notes);
        Assert.Equal((userEmail, userEmail), (card.CreatedBy, card.UpdatedBy));
        Assert.Empty(card.Reservations);
    }

    [Fact]
    public async Task Invalid_details_are_refused_with_every_reason()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-ongeldig@hopsakee.test", UserRole.Staff);

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var result = await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().AddAsync(
            Details("ongeldig@example.test", first: "") with { Kind = CustomerKind.Business, Phone = null }, user.Id);

        Assert.Null(result.CustomerId);
        Assert.Equal(["De bedrijfsnaam is verplicht voor een zakelijke klant.", "De voornaam is verplicht.", "Het telefoonnummer is verplicht."], result.Errors);
    }

    [Fact]
    public async Task A_second_customer_with_the_same_email_address_is_refused_with_a_link_to_the_existing_one()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-dubbel@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var first = await customers.AddAsync(Details("eerst@example.test"), user.Id);

        var second = await customers.AddAsync(Details(" EERST@example.test", first: "Tweede"), user.Id);

        Assert.Null(second.CustomerId);
        Assert.Equal(first.CustomerId, second.ExistingCustomerId);
        Assert.Equal(["Er is al een klant met dit e-mailadres."], second.Errors);
    }

    [Fact]
    public async Task The_same_email_address_can_be_a_customer_of_another_rental_company()
    {
        var hopsakee = await app.CreateUserAsync("hopsakee", "klant-tenant@hopsakee.test", UserRole.Staff);
        var andere = await app.CreateUserAsync("andere", "klant-tenant@andere.test", UserRole.Staff);

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().AddAsync(Details("twee-verhuurders@example.test"), hopsakee.Id)).CustomerId);
        await using (var scope = await app.TenantScopeAsync("andere"))
        {
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            Assert.NotNull((await customers.AddAsync(Details("twee-verhuurders@example.test"), andere.Id)).CustomerId);
            Assert.Single((await customers.SearchAsync(new CustomerSearch("twee-verhuurders"))).Items);
        }
    }

    [Fact]
    public async Task Changing_the_email_address_works_unless_another_customer_has_it()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-wijzig@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var anna = (await customers.AddAsync(Details("anna@example.test", first: "Anna"), user.Id)).CustomerId!.Value;
        var bart = (await customers.AddAsync(Details("bart@example.test", first: "Bart"), user.Id)).CustomerId!.Value;

        var taken = await customers.UpdateAsync(anna, Details("Bart@example.test", first: "Anna"), user.Id);
        var changed = await customers.UpdateAsync(anna, Details("anna.nieuw@example.test", first: "Anna"), user.Id);

        Assert.Equal(bart, taken.ExistingCustomerId);
        Assert.Equal(["Er is al een klant met dit e-mailadres."], taken.Errors);
        Assert.Equal(anna, changed.CustomerId);
        Assert.Equal("anna.nieuw@example.test", (await customers.GetAsync(anna))!.Customer.Email);
    }

    [Fact]
    public async Task Reserving_on_the_website_makes_a_customer_via_website_and_reserving_again_keeps_the_note()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-notitie@hopsakee.test", UserRole.Staff);
        await ReserveAsync("notitie@example.test", "Nina", TestReservations.FutureDay(301), ReservationActor.Website);

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = Assert.Single((await customers.SearchAsync(new CustomerSearch("notitie@example.test"))).Items).Id;
        var card = (await customers.GetAsync(id))!;
        Assert.Equal((null, null), (card.CreatedBy, card.UpdatedBy));
        await customers.UpdateAsync(id, Details("notitie@example.test", first: "Nina") with { Notes = "Betaalt altijd cash" }, user.Id);

        await ReserveAsync("Notitie@example.test", "Nina-Marie", TestReservations.FutureDay(302), ReservationActor.Website);

        card = (await customers.GetAsync(id))!;
        Assert.Equal(("Nina-Marie", "Betaalt altijd cash"), (card.Customer.FirstName, card.Customer.Notes));
        Assert.Equal((null, (string?)null), (card.CreatedBy, card.UpdatedBy));
        Assert.True(card.Customer.UpdatedAtUtc > card.Customer.CreatedAtUtc);
        Assert.Equal(2, card.Reservations.Count);
    }

    [Fact]
    public async Task A_reservation_in_the_back_office_names_the_user_who_made_or_changed_the_customer()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-backoffice@hopsakee.test", UserRole.Staff);
        await ReserveAsync("backoffice@example.test", "Bo", TestReservations.FutureDay(303), ReservationActor.User(user.Id));

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var card = (await customers.GetAsync(Assert.Single((await customers.SearchAsync(new CustomerSearch("backoffice@example.test"))).Items).Id))!;
        Assert.Equal(("klant-backoffice@hopsakee.test", "klant-backoffice@hopsakee.test"), (card.CreatedBy, card.UpdatedBy));
    }

    [Fact]
    public async Task The_customer_card_shows_cancelled_reservations_marketing_consent_and_review_opt_out()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-fiche@hopsakee.test", UserRole.Staff);
        var kept = await ReserveAsync("fiche@example.test", "Fien", TestReservations.FutureDay(310), ReservationActor.Website);
        var cancelled = await ReserveAsync("fiche@example.test", "Fien", TestReservations.FutureDay(320), ReservationActor.Website);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(cancelled, ReservationStatus.Cancelled, ReservationActor.User(user.Id));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.MarketingContacts.Add(new MarketingContact { Email = "fiche@example.test", Culture = "nl", ConsentedAtUtc = DateTime.UtcNow, Source = MarketingContactSource.Reservation });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IReviewOptOuts>().OptOutAsync("fiche@example.test");

        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var listed = Assert.Single((await customers.SearchAsync(new CustomerSearch("fiche@example.test"))).Items);
        var card = (await customers.GetAsync(listed.Id))!;

        Assert.Equal([cancelled, kept], card.Reservations.Select(r => r.Id));
        Assert.Equal(ReservationStatus.Cancelled, card.Reservations[0].Status);
        Assert.True(card.IsMarketingContact);
        Assert.True(card.OptedOutOfReviewRequests);
        // In de lijst tellen geannuleerde reservaties niet mee.
        var day = TestReservations.FutureDay(310);
        Assert.Equal((1, new DateRange(day, day)), (listed.ReservationCount, listed.LastPeriod));
    }

    [Fact]
    public async Task Search_finds_customers_by_name_company_email_phone_or_city_and_filters_on_kind()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-zoek@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var particulier = (await customers.AddAsync(Details("zoek-gerda@example.test", first: "Gerda", last: "Zoekmans",
            address: new Address { Street = "Dorp 1", PostalCode = "8999", City = "Zoekegem" }) with { Phone = "0499 88 77 66" }, user.Id)).CustomerId;
        var zakelijk = (await customers.AddAsync(Details("zoek-chiro@example.test", first: "Wout", last: "Leiding") with
        {
            Kind = CustomerKind.Business, CompanyName = "Chiro Zoekhout",
        }, user.Id)).CustomerId;

        async Task<IEnumerable<Guid?>> Found(string text, CustomerKind? kind = null) =>
            (await customers.SearchAsync(new CustomerSearch(text, kind))).Items.Select(i => (Guid?)i.Id);

        Assert.Equal([particulier], await Found("gerda zoekmans"));
        Assert.Equal([particulier], await Found("ZOEKEGEM"));
        Assert.Equal([particulier], await Found("0499 88 77"));
        Assert.Equal([particulier], await Found("zoek-gerda@"));
        Assert.Equal([zakelijk], await Found("chiro zoekhout"));
        Assert.Equal([zakelijk], await Found("zoek", CustomerKind.Business));
        Assert.Contains(particulier, await Found("zoek", CustomerKind.Private));
        Assert.DoesNotContain(zakelijk, await Found("zoek", CustomerKind.Private));
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        var user = await app.CreateUserAsync("hopsakee", "klant-pagina@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        for (var i = 1; i <= 3; i++)
            await customers.AddAsync(Details($"pagina{i}@example.test", last: $"Paginaklant{i}"), user.Id);

        var second = await customers.SearchAsync(new CustomerSearch("paginaklant", Page: 1, PageSize: 2));

        Assert.Equal(3, second.TotalCount);
        Assert.Equal("Paginaklant3", Assert.Single(second.Items).LastName);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task Both_roles_find_the_customers_in_the_menu_and_open_the_list_and_a_customer_card(UserRole role)
    {
        var client = await app.LoggedInClientAsync($"klanten-{role}@hopsakee.test".ToLowerInvariant(), role);
        var user = await app.CreateUserAsync("hopsakee", $"klanten-{role}@hopsakee.test".ToLowerInvariant(), role);
        Guid id;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            id = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>()
                .AddAsync(Details($"pagina-{role}@example.test".ToLowerInvariant(), last: "Fichemans"), user.Id)).CustomerId!.Value;

        var reservations = await client.GetStringAsync("/admin/reservaties");
        var list = await client.GetAsync("/admin/klanten");
        var card = await client.GetAsync($"/admin/klanten/{id}");
        var added = await client.GetAsync("/admin/klanten/nieuw");

        Assert.Contains("href=\"admin/klanten\"", reservations);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (list.StatusCode, card.StatusCode, added.StatusCode));
        Assert.Contains("Fichemans", WebUtility.HtmlDecode(await card.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Without_logging_in_the_customers_are_not_available()
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync("/admin/klanten");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/inloggen", response.Headers.Location!.OriginalString);
    }

    private async Task<Guid> ReserveAsync(string email, string firstName, DateOnly day, ReservationActor actor)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new RentalItem
        {
            Slug = $"klant-{Guid.NewGuid():N}", Stock = 5, Pricing = new Pricing { DayPrice = 100 },
            Translations = [new Translation { Culture = "nl", Name = "Klantkasteel" }],
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day), [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, "Klant", email, "0470 12 34 56", Gent),
            DeliveryMethod.Pickup, null, null, "nl"), actor);
        return result.Reservation!.Id;
    }
}
