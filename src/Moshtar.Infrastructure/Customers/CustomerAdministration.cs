using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Customers;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Customers;

/// <summary>Met een eigen, kortlevende context per oproep, zodat de interactieve pagina's Klanten en Klantfiche ze kunnen gebruiken.</summary>
internal sealed class CustomerAdministration(IDbContextFactory<AppDbContext> contexts, TimeProvider clock, IVatRegistry vatRegistry) : ICustomerAdministration
{
    private const string NotFound = "Deze klant bestaat niet (meer).";
    private const string AdministratorOnly = "Alleen de beheerder kan klanten verwijderen of anonimiseren.";

    /// <summary>Zoekt klanten op tekst of telefoonnummer en type, op naam gesorteerd, één pagina tegelijk.</summary>
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
                c.Id, c.Kind, c.FirstName, c.LastName, c.CompanyName, c.Email, c.Phone, c.Address.City, c.VatCheck,
                Count = db.Reservations.Count(r => r.CustomerId == c.Id && r.Status != ReservationStatus.Cancelled),
                Last = db.Reservations.Where(r => r.CustomerId == c.Id && r.Status != ReservationStatus.Cancelled)
                    .OrderByDescending(r => r.StartDate).Select(r => new { r.StartDate, r.EndDate }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return new CustomerPage(
            page.Select(c => new CustomerListItem(c.Id, c.Kind, c.FirstName, c.LastName, c.CompanyName, c.Email, c.Phone, c.City,
                c.Count, c.Last is null ? null : new DateRange(c.Last.StartDate, c.Last.EndDate), c.VatCheck)).ToList(),
            total);
    }

    /// <summary>De klantfiche met al zijn reservaties; null als de klant niet (meer) bestaat.</summary>
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

    /// <summary>Voegt een klant toe namens deze gebruiker, met de controle van een nieuw EU-btw-nummer.</summary>
    public Task<CustomerSaveResult> AddAsync(CustomerDetails details, Guid userId, CancellationToken ct = default) =>
        SaveAsync(null, details, userId, ct);

    /// <summary>Wijzigt een klant namens deze gebruiker; een klant die niet (meer) bestaat of geanonimiseerd is, wordt geweigerd.</summary>
    public Task<CustomerSaveResult> UpdateAsync(Guid customerId, CustomerDetails details, Guid userId, CancellationToken ct = default) =>
        SaveAsync(customerId, details, userId, ct);

    /// <summary>Vraagt de btw-databank naar een ingegeven btw-nummer, zonder iets te bewaren.</summary>
    public async Task<VatNumberLookup> LookUpVatNumberAsync(string? vatNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vatNumber) || !VatNumberFormat.IsWellFormed(vatNumber)) return new VatNumberLookup(null);
        var number = VatNumberFormat.Normalize(vatNumber)!;
        if (!VatNumberFormat.IsEu(number)) return new VatNumberLookup(VatCheckStatus.NotCheckable);
        var answer = await vatRegistry.LookUpAsync(number, ct);
        return answer.Outcome switch
        {
            VatRegistryOutcome.Valid => new VatNumberLookup(VatCheckStatus.Valid, answer.Name,
                VatRegistryAddress.Parse(answer.Address, number[..2])),
            VatRegistryOutcome.Invalid => new VatNumberLookup(VatCheckStatus.Invalid),
            _ => new VatNumberLookup(VatCheckStatus.NotChecked),
        };
    }

    /// <summary>"Opnieuw controleren": vraagt de btw-databank opnieuw naar het EU-btw-nummer van de klant en bewaart het antwoord.</summary>
    public async Task<VatRecheckResult> RecheckVatNumberAsync(Guid customerId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return new VatRecheckResult(null, NotFound);
        if (customer.VatNumber is not { } number) return new VatRecheckResult(null, "Deze klant heeft geen btw-nummer.");
        if (!VatNumberFormat.IsEu(number))
            return new VatRecheckResult(customer.VatCheck, "Een btw-nummer van buiten de EU kan de btw-databank niet controleren.");
        var answer = await vatRegistry.LookUpAsync(number, ct);
        // Geen wijziging door een gebruiker: aangemaakt en gewijzigd blijven zoals ze waren.
        customer.RecordVatCheck(answer, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return new VatRecheckResult(customer.VatCheck, answer.Outcome == VatRegistryOutcome.Unavailable
            ? "De btw-databank is nu onbereikbaar. Probeer het later opnieuw."
            : null);
    }

    /// <summary>Verwijdert een klant zonder reservaties; alleen een beheerder mag dat.</summary>
    public async Task<CustomerRemovalResult> DeleteAsync(Guid customerId, Guid userId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!await CustomerChecks.IsAdministratorAsync(db, userId, ct)) return new CustomerRemovalResult([AdministratorOnly]);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return new CustomerRemovalResult([NotFound]);
        if (await db.Reservations.AnyAsync(r => r.CustomerId == customerId, ct))
            return new CustomerRemovalResult(["Deze klant heeft reservaties en kan niet verwijderd worden. Anonimiseer hem in de plaats."]);
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return new CustomerRemovalResult([]);
    }

    /// <summary>Anonimiseert een klant met reservaties die geen van alle nog lopen; alleen een beheerder mag dat.</summary>
    public async Task<CustomerRemovalResult> AnonymizeAsync(Guid customerId, Guid userId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!await CustomerChecks.IsAdministratorAsync(db, userId, ct)) return new CustomerRemovalResult([AdministratorOnly]);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return new CustomerRemovalResult([NotFound]);
        if (customer.IsAnonymized) return new CustomerRemovalResult(["Deze klant is al geanonimiseerd."]);
        var reservations = await db.Reservations.Where(r => r.CustomerId == customerId).ToListAsync(ct);
        if (reservations.Count == 0)
            return new CustomerRemovalResult(["Deze klant heeft geen reservaties. Verwijder hem in de plaats."]);
        // Een levering of afhaling die nog moet gebeuren, kan niet zonder naam, telefoon en adres.
        if (reservations.Any(r => r.OccupiesStock))
            return new CustomerRemovalResult(["Deze klant heeft nog een lopende reservatie. Annuleer ze of rond ze af voor je hem anonimiseert."]);

        var email = customer.Email;
        customer.Anonymize(userId, clock.GetUtcNow().UtcDateTime);
        foreach (var reservation in reservations)
        {
            // De gemeente blijft, voor de cijfers; de straat en het punt op de kaart wijzen de klant aan.
            if (reservation.DeliveryAddress is { } address)
                reservation.DeliveryAddress = new Address { PostalCode = address.PostalCode, City = address.City, Country = address.Country };
            reservation.DeliveryPoint = null;
            reservation.DeliveryPointConfirmed = false;
            reservation.Notes = null;
        }
        // Zijn toestemming voor nieuws en acties vervalt; een afmelding voor reviewvragen blijft, zodat hij er nooit
        // per ongeluk een krijgt als hij later opnieuw klant wordt.
        db.MarketingContacts.RemoveRange(await db.MarketingContacts.Where(m => m.Email == email).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return new CustomerRemovalResult([]);
    }

    private async Task<CustomerSaveResult> SaveAsync(Guid? customerId, CustomerDetails details, Guid userId, CancellationToken ct)
    {
        var errors = details.Errors();
        if (errors.Count > 0) return new CustomerSaveResult(null, errors);

        await using var db = await contexts.CreateDbContextAsync(ct);
        var email = Customer.NormalizeEmail(details.Email);
        if (await OtherWithEmailAsync(db, email, customerId, ct) is { } existing)
            return new CustomerSaveResult(null, [CustomerChecks.DuplicateEmail], existing);

        Customer customer;
        if (customerId is { } id)
        {
            var found = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (found is null) return new CustomerSaveResult(null, [NotFound]);
            customer = found;
            if (customer.IsAnonymized)
                return new CustomerSaveResult(null, ["Deze klant is geanonimiseerd en kan niet meer gewijzigd worden."]);
        }
        else
            db.Customers.Add(customer = new Customer());
        customer.Apply(details);
        // Een nieuw EU-btw-nummer, of een dat nog niet gecontroleerd werd, gaat naar de btw-databank; ongeldig wordt
        // niet bewaard, onbereikbaar wel ("nog niet gecontroleerd").
        if (customer.AwaitsVatCheck)
        {
            var answer = await vatRegistry.LookUpAsync(customer.VatNumber!, ct);
            if (answer.Outcome == VatRegistryOutcome.Invalid) return new CustomerSaveResult(null, [CustomerDetails.UnknownVatNumber]);
            customer.RecordVatCheck(answer, clock.GetUtcNow().UtcDateTime);
        }
        customer.RecordChange(userId, clock.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Intussen kreeg een andere klant hetzelfde e-mailadres, bv. door een reservatie op de website.
            if (await OtherWithEmailAsync(db, email, customerId, ct) is not { } raced) throw;
            return new CustomerSaveResult(null, [CustomerChecks.DuplicateEmail], raced);
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
