using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

/// <summary>
/// De btw-controle bij de btw-databank in het back office, met een nagebootste databank: een geldig nummer vult
/// bedrijfsnaam en factuuradres in, een ongeldig nummer kan niet bewaard worden, een onbereikbare databank laat het
/// nummer toe als "nog niet gecontroleerd" en Moshtar controleert het later opnieuw, een nummer van buiten de EU is
/// niet te controleren.
/// </summary>
public sealed class VatCheckTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Gent = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };

    private static CustomerDetails Business(string email, string vatNumber, string company = "De Linde") =>
        new(CustomerKind.Business, "Els", "Peeters", email, "0470 12 34 56", Gent, "nl", company, vatNumber);

    [Fact]
    public async Task A_valid_number_is_saved_as_valid_with_the_official_name_and_address_apart_from_what_the_user_typed()
    {
        app.VatRegistry.Valid("BE0100000001", "VZW BASISSCHOOL DE LINDE", "SCHOOLSTRAAT 3\n9050 GENTBRUGGE");
        var user = await app.CreateUserAsync("hopsakee", "btw-geldig@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        var result = await customers.AddAsync(Business("geldig@school.test", "BE 0100.000.001", company: "Basisschool De Linde"), user.Id);

        var customer = (await customers.GetAsync(Assert.IsType<Guid>(result.CustomerId)))!.Customer;
        Assert.Equal((VatCheckStatus.Valid, "Basisschool De Linde"), (customer.VatCheck, customer.CompanyName));
        Assert.Equal(("VZW BASISSCHOOL DE LINDE", "SCHOOLSTRAAT 3\n9050 GENTBRUGGE"), (customer.VatRegistryName, customer.VatRegistryAddress));
        Assert.NotNull(customer.VatCheckedAtUtc);
    }

    [Fact]
    public async Task An_invalid_number_cannot_be_saved()
    {
        app.VatRegistry.Answer("BE0100000002", VatRegistryAnswer.Invalid);
        var user = await app.CreateUserAsync("hopsakee", "btw-ongeldig@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        var result = await customers.AddAsync(Business("ongeldig@school.test", "BE0100000002"), user.Id);

        Assert.Null(result.CustomerId);
        Assert.Equal(["Dit btw-nummer is ongeldig: de btw-databank van de EU kent het niet. Verbeter het of laat het leeg."], result.Errors);
        Assert.Equal(0, (await customers.SearchAsync(new CustomerSearch("ongeldig@school.test"))).TotalCount);
    }

    [Fact]
    public async Task When_the_registry_is_unavailable_the_number_is_saved_as_not_yet_checked()
    {
        var user = await app.CreateUserAsync("hopsakee", "btw-onbereikbaar@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        var result = await customers.AddAsync(Business("onbereikbaar@school.test", "BE0100000003"), user.Id);

        var customer = (await customers.GetAsync(Assert.IsType<Guid>(result.CustomerId)))!.Customer;
        Assert.Equal((VatCheckStatus.NotChecked, null), (customer.VatCheck, customer.VatRegistryName));
        Assert.Equal(1, app.VatRegistry.LookupsOf("BE0100000003"));
    }

    [Fact]
    public async Task A_number_from_outside_the_eu_is_saved_as_not_checkable_without_asking_the_registry()
    {
        var user = await app.CreateUserAsync("hopsakee", "btw-buiten-eu@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        var result = await customers.AddAsync(Business("zwitserland@school.test", "CHE-123.456.789"), user.Id);

        var customer = (await customers.GetAsync(Assert.IsType<Guid>(result.CustomerId)))!.Customer;
        Assert.Equal(("CHE123456789", VatCheckStatus.NotCheckable), (customer.VatNumber, customer.VatCheck));
        Assert.Equal(0, app.VatRegistry.LookupsOf("CHE123456789"));
    }

    [Fact]
    public async Task Saving_a_customer_again_with_the_same_checked_number_does_not_ask_the_registry_again()
    {
        app.VatRegistry.Valid("BE0100000004", "BV DUBBEL");
        var user = await app.CreateUserAsync("hopsakee", "btw-zelfde@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = (await customers.AddAsync(Business("zelfde@school.test", "BE0100000004"), user.Id)).CustomerId!.Value;

        await customers.UpdateAsync(id, Business("zelfde@school.test", "BE 0100 000 004") with { Notes = "Belt altijd op maandag" }, user.Id);

        var customer = (await customers.GetAsync(id))!.Customer;
        Assert.Equal((VatCheckStatus.Valid, "BV DUBBEL"), (customer.VatCheck, customer.VatRegistryName));
        Assert.Equal(1, app.VatRegistry.LookupsOf("BE0100000004"));
    }

    [Fact]
    public async Task Entering_a_valid_number_gives_the_official_name_and_billing_address_to_fill_in()
    {
        app.VatRegistry.Valid("BE0100000005", "VZW CHIRO WIEZEWA", "KERKSTRAAT 12\n2000 ANTWERPEN");
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();

        var lookup = await customers.LookUpVatNumberAsync(" be0100.000.005");

        Assert.Equal((VatCheckStatus.Valid, "VZW CHIRO WIEZEWA"), (lookup.Status, lookup.Name));
        Assert.Equal(("KERKSTRAAT 12", "2000", "ANTWERPEN", "BE"),
            (lookup.Address?.Street, lookup.Address?.PostalCode, lookup.Address?.City, lookup.Address?.Country));
    }

    [Fact]
    public async Task Checking_again_records_a_number_that_became_invalid_and_the_list_marks_it()
    {
        app.VatRegistry.Valid("BE0100000008", "BV STOPT ERMEE");
        var user = await app.CreateUserAsync("hopsakee", "btw-opnieuw@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = (await customers.AddAsync(Business("opnieuw@stopt.test", "BE0100000008"), user.Id)).CustomerId!.Value;
        app.VatRegistry.Answer("BE0100000008", VatRegistryAnswer.Invalid);

        var recheck = await customers.RecheckVatNumberAsync(id);

        Assert.Equal(new VatRecheckResult(VatCheckStatus.Invalid), recheck);
        Assert.Equal((VatCheckStatus.Invalid, null), ((await customers.GetAsync(id))!.Customer.VatCheck, (await customers.GetAsync(id))!.Customer.VatRegistryName));
        Assert.Equal(VatCheckStatus.Invalid, Assert.Single((await customers.SearchAsync(new CustomerSearch("opnieuw@stopt.test"))).Items).VatCheck);
    }

    [Fact]
    public async Task Checking_again_while_the_registry_is_unavailable_keeps_the_earlier_answer()
    {
        app.VatRegistry.Valid("BE0100000009", "BV BLIJFT");
        var user = await app.CreateUserAsync("hopsakee", "btw-blijft@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var id = (await customers.AddAsync(Business("blijft@school.test", "BE0100000009"), user.Id)).CustomerId!.Value;
        app.VatRegistry.Answer("BE0100000009", VatRegistryAnswer.Unavailable);

        var recheck = await customers.RecheckVatNumberAsync(id);

        Assert.Equal(new VatRecheckResult(VatCheckStatus.Valid, "De btw-databank is nu onbereikbaar. Probeer het later opnieuw."), recheck);
        Assert.Equal("BV BLIJFT", (await customers.GetAsync(id))!.Customer.VatRegistryName);
    }

    [Fact]
    public async Task A_number_that_was_not_yet_checked_is_checked_again_in_the_background_but_not_more_than_once_an_hour()
    {
        await app.CreateTenantAsync("btw-later", "btw-later.test");
        var user = await app.CreateUserAsync("btw-later", "btw-later@btw-later.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("btw-later");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var valid = (await customers.AddAsync(Business("later-geldig@school.test", "BE0100000010"), user.Id)).CustomerId!.Value;
        var invalid = (await customers.AddAsync(Business("later-ongeldig@school.test", "BE0100000011"), user.Id)).CustomerId!.Value;
        var stillDown = (await customers.AddAsync(Business("later-onbereikbaar@school.test", "BE0100000012"), user.Id)).CustomerId!.Value;
        app.VatRegistry.Valid("BE0100000010", "BV LATER");
        app.VatRegistry.Answer("BE0100000011", VatRegistryAnswer.Invalid);
        var rechecks = scope.ServiceProvider.GetRequiredService<IVatRechecks>();

        var tooSoon = await rechecks.RecheckAsync(DateTime.UtcNow);
        var later = await rechecks.RecheckAsync(DateTime.UtcNow.AddHours(2));

        Assert.Equal((0, 3), (tooSoon, later));
        Assert.Equal((VatCheckStatus.Valid, "BV LATER"), ((await customers.GetAsync(valid))!.Customer.VatCheck, (await customers.GetAsync(valid))!.Customer.VatRegistryName));
        // Een ongeldig nummer houdt de klant niet tegen: het staat gemarkeerd op de klantfiche en in de lijst.
        Assert.Equal(VatCheckStatus.Invalid, (await customers.GetAsync(invalid))!.Customer.VatCheck);
        Assert.Equal(VatCheckStatus.NotChecked, (await customers.GetAsync(stillDown))!.Customer.VatCheck);
        Assert.Equal(2, app.VatRegistry.LookupsOf("BE0100000012"));
    }

    [Fact]
    public async Task The_customer_card_shows_the_check_the_official_data_and_a_button_to_check_again()
    {
        app.VatRegistry.Valid("BE0100000013", "VZW DE SPRONG", "DORPSSTRAAT 4\n9000 GENT");
        var client = await app.LoggedInClientAsync("btw-fiche@hopsakee.test", UserRole.Staff);
        var user = await app.CreateUserAsync("hopsakee", "btw-fiche-maker@hopsakee.test", UserRole.Staff);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var id = (await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>()
            .AddAsync(Business("fiche@sprong.test", "BE0100000013", company: "De Sprong"), user.Id)).CustomerId;

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/klanten/{id}"));

        Assert.Contains("Btw-nummer: BE0100000013", html);
        Assert.Contains("geldig", html);
        Assert.Contains("Laatst gecontroleerd op", html);
        Assert.Contains("Volgens btw-databank", html);
        Assert.Contains("VZW DE SPRONG", html);
        Assert.Contains("Opnieuw controleren", html);
    }

    [Fact]
    public void The_customer_form_takes_the_official_name_and_address_as_company_name_and_billing_address_that_stay_editable()
    {
        var form = new Moshtar.Web.Models.CustomerForm { Kind = CustomerKind.Business, CompanyName = "Chiro", Street = "Markt 1", PostalCode = "9000", City = "Gent" };

        form.Fill(new VatNumberLookup(VatCheckStatus.Valid, "VZW CHIRO WIEZEWA",
            new Address { Street = "KERKSTRAAT 12", PostalCode = "2000", City = "ANTWERPEN", Country = "BE" }));

        Assert.Equal(("VZW CHIRO WIEZEWA", false), (form.CompanyName, form.SameBillingAddress));
        Assert.Equal(("KERKSTRAAT 12", "2000", "ANTWERPEN", "BE"), (form.BillingStreet, form.BillingPostalCode, form.BillingCity, form.BillingCountry));
        Assert.Equal(("Markt 1", "Gent"), (form.Street, form.City));
    }

    [Fact]
    public void The_customer_form_keeps_the_same_billing_address_when_the_official_address_is_the_address()
    {
        var form = new Moshtar.Web.Models.CustomerForm { Kind = CustomerKind.Business, CompanyName = "Chiro", Street = "Kerkstraat 12", PostalCode = "2000", City = "Antwerpen" };

        form.Fill(new VatNumberLookup(VatCheckStatus.Valid, null,
            new Address { Street = "KERKSTRAAT 12", PostalCode = "2000", City = "ANTWERPEN", Country = "BE" }));

        Assert.Equal(("Chiro", true), (form.CompanyName, form.SameBillingAddress));
    }

    [Theory]
    [InlineData("BE0100000006", VatCheckStatus.Invalid)]
    [InlineData("BE0100000007", VatCheckStatus.NotChecked)]
    [InlineData("CHE-123.456.789", VatCheckStatus.NotCheckable)]
    [InlineData("BE12", null)]
    [InlineData("", null)]
    public async Task Entering_another_number_says_what_the_registry_thinks_of_it(string vatNumber, VatCheckStatus? status)
    {
        app.VatRegistry.Answer("BE0100000006", VatRegistryAnswer.Invalid);
        await using var scope = await app.TenantScopeAsync("hopsakee");

        var lookup = await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>().LookUpVatNumberAsync(vatNumber);

        Assert.Equal((status, null, null), (lookup.Status, lookup.Name, lookup.Address));
    }
}
