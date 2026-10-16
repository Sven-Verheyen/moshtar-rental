using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Customers;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// De beheerder downloadt zijn klantenbestand als CSV en importeert klanten uit een CSV met dezelfde kolommen: eerst
/// een voorbeeld, dan bevestigen, met een verslag van elke klant die niet geïmporteerd werd.
/// </summary>
public sealed class CustomerTransferTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private const string Header =
        "Type;Voornaam;Achternaam;Bedrijfsnaam;Btw-nummer;E-mail;Telefoon;Straat;Postcode;Gemeente;Land;" +
        "Factuurstraat;Factuurpostcode;Factuurgemeente;Factuurland;Taal;Notitie";

    private static readonly Address Gent = new() { Street = "Markt 1", PostalCode = "9000", City = "Gent" };

    [Fact]
    public async Task The_administrator_exports_the_customers_as_csv_without_anonymized_customers_or_those_of_another_rental_company()
    {
        var admin = await app.CreateUserAsync("hopsakee", "export-beheerder@hopsakee.test", UserRole.Administrator);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
            await customers.AddAsync(new CustomerDetails(CustomerKind.Business, "Els", "Peeters", "export-school@example.test", "0470 12 34 56", Gent, "fr",
                CompanyName: "School; De Linde", VatNumber: "GB123456789",
                BillingAddress: new Address { Street = "Schoolstraat 3", PostalCode = "9050", City = "Gentbrugge" }, Notes: "=altijd bellen"), admin.Id);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var anonymized = new Customer();
            anonymized.Apply(new CustomerDetails(CustomerKind.Private, "Weg", "Gewist", "export-anoniem@example.test", "0470 12 34 56", Gent, "nl"));
            anonymized.Anonymize(admin.Id, DateTime.UtcNow);
            db.Customers.Add(anonymized);
            await db.SaveChangesAsync();
        }
        await using (var scope = await app.TenantScopeAsync("andere"))
        {
            var other = await app.CreateUserAsync("andere", "export-andere@andere.test", UserRole.Administrator);
            await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>()
                .AddAsync(new CustomerDetails(CustomerKind.Private, "Jan", "Andere", "export-andere@example.test", "0470 12 34 56", Gent, "nl"), other.Id);
        }
        var client = await app.LoggedInClientAsync("export-beheerder-http@hopsakee.test", UserRole.Administrator);

        var response = await client.GetAsync("/admin/klanten.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal(Header, lines[0]);
        Assert.Contains("Zakelijk;Els;Peeters;\"School; De Linde\";GB123456789;export-school@example.test;'+32470123456;Markt 1;9000;Gent;BE;" +
            "Schoolstraat 3;9050;Gentbrugge;BE;fr;'=altijd bellen", lines);
        Assert.DoesNotContain(lines, l => l.Contains("export-andere@example.test") || l.Contains(Customer.AnonymizedEmailDomain));
    }

    [Fact]
    public async Task The_preview_counts_the_customers_to_add_and_lists_every_skipped_row_with_its_reason_without_saving_anything()
    {
        var admin = await app.CreateUserAsync("hopsakee", "voorbeeld-beheerder@hopsakee.test", UserRole.Administrator);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        await customers.AddAsync(new CustomerDetails(CustomerKind.Private, "Al", "Klant", "voorbeeld-bestaand@example.test", "0470 12 34 56", Gent, "nl"), admin.Id);
        var csv = Csv(
            "Particulier;Els;Peeters;;;voorbeeld-els@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;",
            "Zakelijk;Jan;Smet;\"Basisschool \"\"De Linde\"\"\";BE0123456789;voorbeeld-school@example.test;0470 12 34 56;Schoolstraat 3;9050;Gentbrugge;BE;;;;;fr;\"Bel eerst;\naltijd\"",
            "Particulier;Al;Klant;;;Voorbeeld-Bestaand@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;",
            "Particulier;Els;Dubbel;;;voorbeeld-els@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;",
            "Particulier;Piet;;;;voorbeeld-piet@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;",
            "Zakelijk;Bart;Btw;Bart BV;BE123;voorbeeld-btw@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;",
            "Bedrijf;Ann;Type;;;voorbeeld-type@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;de;");

        var preview = await scope.ServiceProvider.GetRequiredService<ICustomerTransfer>().PreviewAsync(csv);

        Assert.Empty(preview.Errors);
        Assert.Equal(2, preview.Count);
        Assert.Equal(
        [
            new SkippedCustomer(5, "Al Klant", "Voorbeeld-Bestaand@example.test", "Er is al een klant met dit e-mailadres."),
            new SkippedCustomer(6, "Els Dubbel", "voorbeeld-els@example.test", "Dit e-mailadres staat al op regel 2 van dit bestand."),
            new SkippedCustomer(7, "Piet", "voorbeeld-piet@example.test", "De achternaam is verplicht."),
            new SkippedCustomer(8, "Bart BV", "voorbeeld-btw@example.test", CustomerDetails.InvalidVatNumber),
            new SkippedCustomer(9, "Ann Type", "voorbeeld-type@example.test",
                "Het type moet Particulier of Zakelijk zijn. De taal moet een van deze zijn: nl, fr, en."),
        ], preview.Skipped);
        Assert.Empty((await customers.SearchAsync(new CustomerSearch("voorbeeld-els@example.test"))).Items);
    }

    [Fact]
    public async Task Importing_adds_the_customers_as_created_by_import_and_the_vat_check_marks_an_unknown_number_later()
    {
        var admin = await app.CreateUserAsync("hopsakee", "import-beheerder@hopsakee.test", UserRole.Administrator);
        app.VatRegistry.Answer("BE0999999999", new VatRegistryAnswer(VatRegistryOutcome.Invalid));
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var csv = Csv(
            "Zakelijk;Jan;Smet;Smet BV;BE 0999.999.999;import-smet@example.test;'+32470123456;Kerkstraat 2;9000;Gent;;Postbus 1;9000;Gent;;;Klant sinds 2019",
            "Particulier;Els;;;;import-zonder-naam@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;");

        var report = await scope.ServiceProvider.GetRequiredService<ICustomerTransfer>().ImportAsync(csv, admin.Id);

        Assert.Equal(1, report.Count);
        var skipped = Assert.Single(report.Skipped);
        Assert.Equal(new SkippedCustomer(3, "Els", "import-zonder-naam@example.test", "De achternaam is verplicht."), skipped);
        var item = Assert.Single((await customers.SearchAsync(new CustomerSearch("import-smet@example.test"))).Items);
        var card = (await customers.GetAsync(item.Id))!;
        var customer = card.Customer;
        Assert.Equal((CustomerKind.Business, "Smet BV", "BE0999999999", "+32470123456"), (customer.Kind, customer.CompanyName, customer.VatNumber, customer.Phone));
        Assert.Equal(("Postbus 1", "BE", "nl", "Klant sinds 2019"), (customer.BillingAddress?.Street, customer.Address.Country, customer.PreferredCulture, customer.Notes));
        Assert.True(customer.CreatedByImport);
        Assert.Equal("import-beheerder@hopsakee.test", card.CreatedBy);
        Assert.Equal(VatCheckStatus.NotChecked, customer.VatCheck);
        Assert.Equal(0, app.VatRegistry.LookupsOf("BE0999999999"));
        var page = WebUtility.HtmlDecode(await (await app.LoggedInClientAsync("import-kijker@hopsakee.test", UserRole.Staff)).GetStringAsync($"/admin/klanten/{item.Id}"));
        Assert.Contains("via import door import-beheerder@hopsakee.test", page);

        await scope.ServiceProvider.GetRequiredService<IVatRechecks>().RecheckAsync(DateTime.UtcNow);

        Assert.Equal(VatCheckStatus.Invalid, Assert.Single((await customers.SearchAsync(new CustomerSearch("import-smet@example.test"))).Items).VatCheck);
    }

    [Fact]
    public void The_report_of_skipped_rows_downloads_as_csv()
    {
        var csv = CustomerCsv.SkippedReport([new SkippedCustomer(7, "Piet", "=piet@example.test", "De achternaam is verplicht.")]);

        Assert.Equal("Regel;Naam;E-mail;Reden\r\n7;Piet;'=piet@example.test;De achternaam is verplicht.\r\n", csv);
    }

    [Fact]
    public async Task An_exported_file_imports_again_once_its_customers_are_gone()
    {
        var admin = await app.CreateUserAsync("hopsakee", "rondje-beheerder@hopsakee.test", UserRole.Administrator);
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerAdministration>();
        var transfer = scope.ServiceProvider.GetRequiredService<ICustomerTransfer>();
        var id = (await customers.AddAsync(new CustomerDetails(CustomerKind.Private, "Rita", "Rondje", "rondje@example.test", "0470 12 34 56", Gent, "en",
            Notes: "-korting, \"vaste klant\""), admin.Id)).CustomerId!.Value;
        var export = await transfer.ExportAsync();
        await customers.DeleteAsync(id, admin.Id);
        // Alleen de rij van Rita: de andere klanten bestaan nog en zouden overgeslagen worden.
        var lines = export.Split("\r\n");
        var csv = lines[0] + "\r\n" + lines.Single(l => l.Contains("rondje@example.test"));

        var report = await transfer.ImportAsync(csv, admin.Id);

        Assert.Equal((1, 0), (report.Count, report.Skipped.Count));
        var item = Assert.Single((await customers.SearchAsync(new CustomerSearch("rondje@example.test"))).Items);
        var customer = (await customers.GetAsync(item.Id))!.Customer;
        Assert.Equal(("+32470123456", "en", "-korting, \"vaste klant\""), (customer.Phone, customer.PreferredCulture, customer.Notes));
    }

    [Fact]
    public async Task A_file_with_commas_and_without_all_columns_is_read_or_refused_as_a_whole()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var transfer = scope.ServiceProvider.GetRequiredService<ICustomerTransfer>();

        var commas = await transfer.PreviewAsync(Header.Replace(';', ',') + "\n" +
            "particulier,Els,Komma,,,komma@example.test,0470 12 34 56,\"Markt 1, bus 2\",9000,Gent,BE,,,,,nl,\n\n");
        var missing = await transfer.PreviewAsync("Voornaam;Achternaam;E-mail\r\nEls;Peeters;els@example.test\r\n");

        Assert.Equal((1, 0, 0), (commas.Count, commas.Skipped.Count, commas.Errors.Count));
        Assert.Equal(["Deze kolommen ontbreken: Type, Bedrijfsnaam, Btw-nummer, Telefoon, Straat, Postcode, Gemeente, Land, Factuurstraat, " +
            "Factuurpostcode, Factuurgemeente, Factuurland, Taal, Notitie. Download het voorbeeldbestand voor de juiste kolommen."], missing.Errors);
    }

    [Fact]
    public async Task Staff_can_neither_import_nor_export_nor_download_the_sample()
    {
        var staff = await app.CreateUserAsync("hopsakee", "import-medewerker@hopsakee.test", UserRole.Staff);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var report = await scope.ServiceProvider.GetRequiredService<ICustomerTransfer>().ImportAsync(
                Csv("Particulier;Els;Peeters;;;import-medewerker@example.test;0470 12 34 56;Markt 1;9000;Gent;BE;;;;;nl;"), staff.Id);
            Assert.Equal(["Alleen de beheerder kan klanten importeren."], report.Errors);
            Assert.Empty((await scope.ServiceProvider.GetRequiredService<ICustomerAdministration>()
                .SearchAsync(new CustomerSearch("import-medewerker@example.test"))).Items);
        }
        var client = await app.LoggedInClientAsync("import-medewerker-http@hopsakee.test", UserRole.Staff);

        foreach (var path in new[] { "/admin/klanten.csv", "/admin/klanten-voorbeeld.csv", "/admin/klanten/importeren" })
        {
            var response = await client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.Redirect)
                response = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.DoesNotContain("klanten/importeren", await client.GetStringAsync("/admin/klanten"));
    }

    [Fact]
    public async Task The_administrator_downloads_a_sample_file_with_the_same_columns_that_imports_cleanly()
    {
        var client = await app.LoggedInClientAsync("voorbeeldbestand-beheerder@hopsakee.test", UserRole.Administrator);

        var response = await client.GetAsync("/admin/klanten-voorbeeld.csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(Header, text[1..].Split("\r\n")[0]);
        Assert.Contains("klanten/importeren", await client.GetStringAsync("/admin/klanten"));
        Assert.Contains("Voorbeeldbestand downloaden", await client.GetStringAsync("/admin/klanten/importeren"));

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var preview = await scope.ServiceProvider.GetRequiredService<ICustomerTransfer>().PreviewAsync(text);
        Assert.Equal((2, 0, 0), (preview.Count, preview.Skipped.Count, preview.Errors.Count));
    }

    private static string Csv(params string[] rows) => "﻿" + Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";
}
