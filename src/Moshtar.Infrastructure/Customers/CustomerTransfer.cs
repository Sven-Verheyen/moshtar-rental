using System.Text;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Customers;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Customers;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Customers;

/// <summary>Met een eigen, kortlevende context per oproep, zodat de interactieve importpagina ze kan gebruiken.</summary>
internal sealed class CustomerTransfer(IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock) : ICustomerTransfer
{
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var anonymized = "%@" + Customer.AnonymizedEmailDomain;
        var customers = await db.Customers.AsNoTracking()
            .Where(c => !EF.Functions.Like(c.Email, anonymized))
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Email)
            .ToListAsync(ct);
        return CustomerCsv.Write(customers);
    }

    public async Task<CustomerImportResult> PreviewAsync(string csv, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var plan = await PlanAsync(db, csv, ct);
        return plan.Result;
    }

    public async Task<CustomerImportResult> ImportAsync(string csv, Guid userId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        // De importpagina is alleen voor de beheerder; de dienst weigert het ook zelf.
        if (!await CustomerChecks.IsAdministratorAsync(db, userId, ct))
            return CustomerImportResult.Refused("Alleen de beheerder kan klanten importeren.");
        var plan = await PlanAsync(db, csv, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var details in plan.Customers)
        {
            var customer = new Customer { CreatedByImport = true };
            // Een EU-btw-nummer is nu "nog niet gecontroleerd"; de achtergrondcontrole vraagt het de btw-databank.
            customer.Apply(details);
            customer.RecordChange(userId, now);
            db.Customers.Add(customer);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (CustomerChecks.IsUniqueViolation(e))
        {
            // Intussen kreeg iemand anders een van deze e-mailadressen, bv. door een reservatie op de website. Er werd
            // niets bewaard; een nieuw voorbeeld slaat die rij over.
            return CustomerImportResult.Refused("Intussen werd een van deze klanten op een andere manier toegevoegd. Er werd niets geïmporteerd; probeer het opnieuw.");
        }
        return plan.Result;
    }

    private async Task<Plan> PlanAsync(AppDbContext db, string csv, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(csv) > ICustomerTransfer.MaxFileSize)
            return Plan.Refused($"Het bestand is te groot: hoogstens {ICustomerTransfer.MaxFileSize / 1024 / 1024} MB.");
        var file = CustomerCsv.Read(csv);
        if (file.Error is { } error) return Plan.Refused(error);
        if (file.Rows.Count == 0) return Plan.Refused("Het bestand bevat geen klanten.");
        if (file.Rows.Count > ICustomerTransfer.MaxRows)
            return Plan.Refused($"Het bestand bevat {file.Rows.Count} klanten; importeer er hoogstens {ICustomerTransfer.MaxRows} tegelijk.");

        var emails = file.Rows.Select(r => Customer.NormalizeEmail(r["E-mail"])).Where(e => e.Length > 0).Distinct().ToList();
        var existing = (await db.Customers.AsNoTracking().Where(c => emails.Contains(c.Email)).Select(c => c.Email).ToListAsync(ct)).ToHashSet();
        var cultures = tenantContext.Tenant?.Cultures ?? ["nl"];
        var defaultCulture = tenantContext.Tenant?.DefaultCulture ?? "nl";

        var accepted = new List<CustomerDetails>();
        var skipped = new List<SkippedCustomer>();
        var firstLine = new Dictionary<string, int>();
        foreach (var row in file.Rows)
        {
            var (details, errors) = CustomerCsv.Details(row, cultures, defaultCulture);
            var email = Customer.NormalizeEmail(details.Email);
            string? reason = errors.Count > 0 ? string.Join(" ", errors)
                : existing.Contains(email) ? CustomerChecks.DuplicateEmail
                : firstLine.TryGetValue(email, out var line) ? $"Dit e-mailadres staat al op regel {line} van dit bestand."
                : null;
            if (reason is null)
            {
                firstLine[email] = row.Line;
                accepted.Add(details);
            }
            else
                skipped.Add(new SkippedCustomer(row.Line, Name(details), row["E-mail"], reason));
        }
        return new Plan(accepted, new CustomerImportResult(accepted.Count, skipped, []));
    }

    private static string Name(CustomerDetails d) =>
        d.Kind == CustomerKind.Business && !string.IsNullOrWhiteSpace(d.CompanyName) ? d.CompanyName.Trim() : $"{d.FirstName.Trim()} {d.LastName.Trim()}".Trim();

    private sealed record Plan(IReadOnlyList<CustomerDetails> Customers, CustomerImportResult Result)
    {
        public static Plan Refused(string error) => new([], CustomerImportResult.Refused(error));
    }
}
