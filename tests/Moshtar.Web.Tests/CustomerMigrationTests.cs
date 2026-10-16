using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;
using Npgsql;

namespace Moshtar.Web.Tests;

/// <summary>
/// Klanten die al reserveerden vóór het klantenbestand bestond, staan er na de migratie in: particulier, of zakelijk als
/// hun btw-nummer gekend is, met "factuuradres is hetzelfde". Dubbels van hetzelfde e-mailadres worden één klant.
/// </summary>
public sealed class CustomerMigrationTests : IAsyncLifetime
{
    private const string BeforeCustomers = "20261016102834_SleutelsInDatabankEnAutomatischeHistoriek";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("MOSHTAR_TEST_DB") ?? "Host=localhost;Port=5432;Username=moshtar;Password=moshtar")
    {
        Database = $"moshtar_test_{Guid.NewGuid():N}",
    }.ConnectionString;

    private AppDbContext _db = null!;

    public Task InitializeAsync()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options, new TenantOf(Tenant));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Existing_customers_become_private_or_business_with_the_same_billing_address_and_duplicates_become_one()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeCustomers);

        var particulier = await InsertCustomerAsync(Tenant, "Piet", "piet@example.test", vat: null);
        var school = await InsertCustomerAsync(Tenant, "Els", "school@example.test", vat: "BE0123456789");
        var oldDuplicate = await InsertCustomerAsync(Tenant, "Oud", " Dubbel@Example.test");
        var newDuplicate = await InsertCustomerAsync(Tenant, "Nieuw", "dubbel@example.test");
        var elsewhere = await InsertCustomerAsync(OtherTenant, "Ander", "dubbel@example.test");
        await InsertReservationAsync(Tenant, particulier, "2026-0001", new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));
        await InsertReservationAsync(Tenant, oldDuplicate, "2026-0002", new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc));
        await InsertReservationAsync(Tenant, newDuplicate, "2026-0003", new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc));

        await migrator.MigrateAsync();

        var customers = await _db.Customers.AsNoTracking().IgnoreQueryFilters().ToListAsync();
        var piet = customers.Single(c => c.Id == particulier);
        Assert.Equal((CustomerKind.Private, null), (piet.Kind, piet.BillingAddress));
        Assert.Equal(new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc), piet.CreatedAtUtc);
        Assert.Null(piet.CreatedByUserId);
        Assert.Equal(CustomerKind.Business, customers.Single(c => c.Id == school).Kind);

        // Van twee klanten met hetzelfde e-mailadres blijft die met de laatste reservatie, met alle reservaties.
        var dubbel = Assert.Single(customers, c => c.TenantId == Tenant && c.Email == "dubbel@example.test");
        Assert.Equal((newDuplicate, "Nieuw"), (dubbel.Id, dubbel.FirstName));
        Assert.Equal(2, await _db.Reservations.IgnoreQueryFilters().CountAsync(r => r.CustomerId == newDuplicate));
        Assert.Equal(new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc), dubbel.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), dubbel.UpdatedAtUtc);
        Assert.Contains(customers, c => c.Id == elsewhere);
    }

    private async Task<Guid> InsertCustomerAsync(Guid tenant, string firstName, string email, string? vat = null)
    {
        var id = Guid.NewGuid();
        await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Customers" ("Id", "TenantId", "FirstName", "LastName", "Email", "Phone", "CompanyName", "VatNumber",
                "PreferredCulture", "Address_Street", "Address_PostalCode", "Address_City", "Address_Country")
            VALUES ({id}, {tenant}, {firstName}, 'Klant', {email}, '+32470123456', NULL, {vat}, 'nl', 'Markt 1', '9000', 'Gent', 'BE')
            """);
        return id;
    }

    private async Task InsertReservationAsync(Guid tenant, Guid customer, string number, DateTime createdAtUtc)
    {
        var id = Guid.NewGuid();
        var day = new DateOnly(2026, 6, 1);
        await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Reservations" ("Id", "TenantId", "Number", "CustomerId", "StartDate", "EndDate", "Status", "DeliveryMethod",
                "DeliveryPointConfirmed", "TotalPrice", "Culture", "CreatedAtUtc")
            VALUES ({id}, {tenant}, {number}, {customer}, {day}, {day}, 1, 1, false, 100, 'nl', {createdAtUtc})
            """);
        await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO "ReservationEvents" ("Id", "TenantId", "ReservationId", "Kind", "Status", "OccurredAtUtc", "UserId", "Automatic")
            VALUES ({Guid.NewGuid()}, {tenant}, {id}, 1, 1, {createdAtUtc}, NULL, false)
            """);
    }

    private sealed class TenantOf(Guid id) : ITenantContext
    {
        public Tenant? Tenant { get; } = new() { Id = id };
    }
}
