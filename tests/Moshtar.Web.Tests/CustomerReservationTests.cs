using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Web.Models;

namespace Moshtar.Web.Tests;

/// <summary>
/// Reserveren met de gegevens van een klant: particulier of zakelijk, met bedrijfsnaam, btw-nummer en een apart
/// factuuradres. Reserveren maakt de klant of werkt hem bij met wat laatst ingevuld werd, behalve de interne notitie.
/// </summary>
public sealed class CustomerReservationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Gent = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };
    private static readonly Address School = new() { Street = "Schoolstraat 3", PostalCode = "9050", City = "Gentbrugge" };

    [Fact]
    public async Task Reserving_as_a_business_keeps_company_vat_number_and_billing_address_and_reserving_again_keeps_the_note()
    {
        var user = await app.CreateUserAsync("hopsakee", "zakelijk-notitie@hopsakee.test", UserRole.Staff);
        await ReserveAsync(Customer("linde@example.test") with
        {
            Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde", VatNumber = "be 0123.456.789", BillingAddress = School,
        }, TestReservations.FutureDay(41));
        var id = await CustomerIdAsync("linde@example.test");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            var customer = (await customers.GetAsync(id))!.Customer;
            Assert.Equal((CustomerKind.Business, "Basisschool De Linde", "BE0123456789"), (customer.Kind, customer.CompanyName, customer.VatNumber));
            Assert.Equal(("Gentbrugge", "Gent"), (customer.BillingAddressOrAddress.City, customer.Address.City));
            await customers.UpdateAsync(id, CustomerForm.From(customer).ToDetails() with { Notes = "Altijd via juf Els" }, user.Id);
        }

        await ReserveAsync(Customer("Linde@example.test", first: "Els") with
        {
            Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde vzw", VatNumber = "BE0123456789",
        }, TestReservations.FutureDay(42));

        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var customer = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().GetAsync(id))!.Customer;
            Assert.Equal(("Els", "Basisschool De Linde vzw", "Altijd via juf Els"), (customer.FirstName, customer.CompanyName, customer.Notes));
            Assert.Null(customer.BillingAddress);
        }
    }

    [Fact]
    public async Task A_business_that_reserves_again_as_a_private_person_loses_its_company_and_vat_number()
    {
        await ReserveAsync(Customer("wordt-particulier@example.test") with
        {
            Kind = CustomerKind.Business, CompanyName = "Chiro Wiezewa", VatNumber = "BE0123456789",
        }, TestReservations.FutureDay(43));

        // Een particulier stuurt soms toch een bedrijfsnaam mee; die hoort niet bij een particuliere klant.
        await ReserveAsync(Customer("wordt-particulier@example.test") with { CompanyName = "Chiro Wiezewa" }, TestReservations.FutureDay(44));

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customer = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().GetAsync(await CustomerIdAsync("wordt-particulier@example.test")))!.Customer;
        Assert.Equal((CustomerKind.Private, null, null), (customer.Kind, customer.CompanyName, customer.VatNumber));
    }

    [Theory]
    [InlineData(" ", null, null, "De bedrijfsnaam is verplicht voor een zakelijke klant.")]
    [InlineData("Chiro", "BE123", null, "Dit is geen geldig btw-nummer. Begin met de landcode, bv. BE0123456789.")]
    [InlineData("Chiro", null, "Schoolstraat 3", "Vul de straat met huisnummer, de postcode en de gemeente van het factuuradres in.")]
    public async Task A_reservation_with_incomplete_business_details_is_refused(string company, string? vat, string? billingStreet, string message)
    {
        var email = $"onvolledig-{message.Length}@example.test";
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(Customer(email) with
        {
            Kind = CustomerKind.Business, CompanyName = company, VatNumber = vat,
            BillingAddress = billingStreet is null ? null : new Address { Street = billingStreet },
        }, TestReservations.FutureDay(45)));

        Assert.Equal(message, refused.Message);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().SearchAsync(new CustomerSearch(email))).Items);
    }

    [Fact]
    public async Task The_delivery_address_is_the_address_of_the_customer_and_stays_when_the_customer_moves()
    {
        var user = await app.CreateUserAsync("hopsakee", "verhuist@hopsakee.test", UserRole.Staff);
        var reservation = await ReserveAsync(Customer("verhuist@example.test"), TestReservations.FutureDay(46), DeliveryMethod.Delivery);
        Assert.Equal("Markt 1", reservation.DeliveryAddress!.Street);

        var id = await CustomerIdAsync("verhuist@example.test");
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var moved = CustomerForm.From((await customers.GetAsync(id))!.Customer);
        moved.Street = "Nieuwstraat 9";
        Assert.True((await customers.UpdateAsync(id, moved.ToDetails(), user.Id)).Succeeded);

        await using var after = await app.TenantScopeAsync("hopsakee");
        var db = after.ServiceProvider.GetRequiredService<Moshtar.Infrastructure.Persistence.AppDbContext>();
        Assert.Equal("Markt 1", db.Reservations.Single(r => r.Id == reservation.Id).DeliveryAddress!.Street);
    }

    [Fact]
    public async Task The_reservation_in_the_back_office_shows_kind_company_vat_number_and_billing_address_of_the_customer()
    {
        var business = await ReserveAsync(Customer("detail-zakelijk@example.test") with
        {
            Kind = CustomerKind.Business, CompanyName = "Basisschool De Linde", VatNumber = "BE0123456789", BillingAddress = School,
        }, TestReservations.FutureDay(17));
        var person = await ReserveAsync(Customer("detail-particulier@example.test"), TestReservations.FutureDay(18));
        var client = await app.LoggedInClientAsync("detail-reservatie@hopsakee.test", UserRole.Staff);

        var businessHtml = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties?nummer={business.Number}"));
        var personHtml = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties?nummer={person.Number}"));

        Assert.Contains("Zakelijke klant: Basisschool De Linde", businessHtml);
        Assert.Contains("Btw-nummer: BE0123456789", businessHtml);
        Assert.Contains("Factuuradres: Schoolstraat 3, 9050 Gentbrugge", businessHtml);
        Assert.Contains($"href=\"admin/klanten/{business.CustomerId}\"", businessHtml);
        Assert.Contains("Particuliere klant", personHtml);
        Assert.Contains("Factuuradres: hetzelfde als het adres", personHtml);
        Assert.DoesNotContain("Btw-nummer", personHtml);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task From_the_customer_card_a_new_reservation_starts_with_the_details_of_that_customer(UserRole role)
    {
        var email = $"vanaf-fiche-{role}@hopsakee.test".ToLowerInvariant();
        var user = await app.CreateUserAsync("hopsakee", email, role);
        var client = await app.LoggedInClientAsync(email, role);
        Guid id;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            id = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().AddAsync(new CustomerDetails(
                CustomerKind.Business, "Wout", "Leiding", $"chiro-{role}@example.test".ToLowerInvariant(), "0499 88 77 66",
                new Address { Street = "Heidestraat 12", PostalCode = "2950", City = "Kapellen" }, "nl",
                $"Chiro Zonnewende {role}", "BE0123456789", School), user.Id)).CustomerId!.Value;

        var card = await client.GetStringAsync($"/admin/klanten/{id}");
        var form = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/reservaties/nieuw?klant={id}"));

        Assert.Contains($"href=\"admin/reservaties/nieuw?klant={id}\"", card);
        foreach (var value in new[] { "Wout", "Leiding", $"chiro-{role}@example.test".ToLowerInvariant(), "+32499887766", "Heidestraat 12", "2950", "Kapellen",
                     $"Chiro Zonnewende {role}", "BE0123456789", "Schoolstraat 3", "Gentbrugge" })
            Assert.Contains($"value=\"{value}\"", form);
    }

    private static ReservationCustomer Customer(string email, string first = "Els") => new(first, "Peeters", email, "0470 12 34 56", Gent);

    private async Task<Guid> CustomerIdAsync(string email)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return Assert.Single((await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().SearchAsync(new CustomerSearch(email))).Items).Id;
    }

    private async Task<Reservation> ReserveAsync(ReservationCustomer customer, DateOnly day, DeliveryMethod method = DeliveryMethod.Pickup)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<Moshtar.Infrastructure.Persistence.AppDbContext>();
        var item = new RentalItem
        {
            Slug = $"zakelijk-{Guid.NewGuid():N}", Stock = 5, Pricing = new Pricing { DayPrice = 100 },
            Translations = [new Translation { Culture = "nl", Name = "Zakelijk kasteel" }],
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day), [new ReservationLineRequest(item.Id, null, 1)], customer, method, null, null, "nl"), ReservationActor.Website);
        return result.Reservation!;
    }
}
