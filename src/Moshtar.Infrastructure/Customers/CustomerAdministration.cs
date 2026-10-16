using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Customers;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Customers;

/// <summary>Met een eigen, kortlevende context per oproep, zodat de interactieve pagina's Klanten en Klantfiche ze kunnen gebruiken.</summary>
internal sealed class CustomerAdministration(IDbContextFactory<AppDbContext> contexts, TimeProvider clock) : ICustomerAdministration
{
    private const string DuplicateEmail = "Er is al een klant met dit e-mailadres.";

    public async Task<CustomerPage> SearchAsync(CustomerSearch search, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var customers = db.Customers.AsNoTracking();
        if (search.Kind is { } kind)
            customers = customers.Where(c => c.Kind == kind);
        if (LooksLikePhone(search.Text))
        {
            // Een telefoonnummer staat in internationaal formaat (+32470…); "0470 12" vindt het dus zonder de 0 vooraan.
            var digits = new string(search.Text!.Where(char.IsDigit).ToArray());
            digits = digits.StartsWith("00") ? digits[2..] : digits.StartsWith('0') ? digits[1..] : digits;
            customers = customers.Where(c => c.Phone != null && c.Phone.Contains(digits));
        }
        else foreach (var word in (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = $"%{Escape(word)}%";
            customers = customers.Where(c =>
                EF.Functions.ILike(c.FirstName, pattern) || EF.Functions.ILike(c.LastName, pattern)
                || EF.Functions.ILike(c.CompanyName ?? "", pattern) || EF.Functions.ILike(c.Email, pattern)
                || EF.Functions.ILike(c.Phone ?? "", pattern) || EF.Functions.ILike(c.Address.City, pattern));
        }

        var total = await customers.CountAsync(ct);
        var page = await customers
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Email)
            .Skip(Math.Max(0, search.Page) * search.PageSize).Take(search.PageSize)
            .Select(c => new
            {
                c.Id, c.Kind, c.FirstName, c.LastName, c.CompanyName, c.Email, c.Phone, c.Address.City,
                Count = db.Reservations.Count(r => r.CustomerId == c.Id && r.Status != ReservationStatus.Cancelled),
                Last = db.Reservations.Where(r => r.CustomerId == c.Id && r.Status != ReservationStatus.Cancelled)
                    .OrderByDescending(r => r.StartDate).Select(r => new { r.StartDate, r.EndDate }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return new CustomerPage(
            page.Select(c => new CustomerListItem(c.Id, c.Kind, c.FirstName, c.LastName, c.CompanyName, c.Email, c.Phone, c.City,
                c.Count, c.Last is null ? null : new DateRange(c.Last.StartDate, c.Last.EndDate))).ToList(),
            total);
    }

    public async Task<CustomerCard?> GetAsync(Guid customerId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return null;

        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.StartDate).ThenByDescending(r => r.Number)
            .Select(r => new { r.Id, r.Number, r.StartDate, r.EndDate, r.Status, r.TotalPrice })
            .ToListAsync(ct);
        var userIds = new[] { customer.CreatedByUserId, customer.UpdatedByUserId }.OfType<Guid>().ToList();
        // Ook uitgeschakelde gebruikers blijven met hun e-mailadres zichtbaar.
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return new CustomerCard(
            customer,
            reservations.Select(r => new CustomerReservation(r.Id, r.Number, new DateRange(r.StartDate, r.EndDate), r.Status, r.TotalPrice)).ToList(),
            await db.MarketingContacts.AnyAsync(m => m.Email == customer.Email, ct),
            await db.ReviewOptOuts.AnyAsync(o => o.Email == customer.Email, ct),
            customer.CreatedByUserId is { } created ? users.GetValueOrDefault(created) : null,
            customer.UpdatedByUserId is { } updated ? users.GetValueOrDefault(updated) : null);
    }

    public Task<CustomerSaveResult> AddAsync(CustomerDetails details, Guid userId, CancellationToken ct = default) =>
        SaveAsync(null, details, userId, ct);

    public Task<CustomerSaveResult> UpdateAsync(Guid customerId, CustomerDetails details, Guid userId, CancellationToken ct = default) =>
        SaveAsync(customerId, details, userId, ct);

    private async Task<CustomerSaveResult> SaveAsync(Guid? customerId, CustomerDetails details, Guid userId, CancellationToken ct)
    {
        var errors = details.Errors();
        if (errors.Count > 0) return new CustomerSaveResult(null, errors);

        await using var db = await contexts.CreateDbContextAsync(ct);
        var email = Customer.NormalizeEmail(details.Email);
        if (await OtherWithEmailAsync(db, email, customerId, ct) is { } existing)
            return new CustomerSaveResult(null, [DuplicateEmail], existing);

        Customer customer;
        if (customerId is { } id)
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new ArgumentException("Klant niet gevonden.");
        else
            db.Customers.Add(customer = new Customer());
        customer.Apply(details);
        customer.RecordChange(userId, clock.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Intussen kreeg een andere klant hetzelfde e-mailadres, bv. door een reservatie op de website.
            if (await OtherWithEmailAsync(db, email, customerId, ct) is not { } raced) throw;
            return new CustomerSaveResult(null, [DuplicateEmail], raced);
        }
        return new CustomerSaveResult(customer.Id, []);
    }

    private static Task<Guid?> OtherWithEmailAsync(AppDbContext db, string email, Guid? customerId, CancellationToken ct) =>
        db.Customers.AsNoTracking().Where(c => c.Email == email && (customerId == null || c.Id != customerId)).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    /// <summary>Alleen cijfers en wat in een telefoonnummer hoort, met minstens drie cijfers.</summary>
    private static bool LooksLikePhone(string? text) =>
        text is not null && text.Count(char.IsDigit) >= 3 && text.Trim().All(ch => char.IsDigit(ch) || " +./()-".Contains(ch));

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
