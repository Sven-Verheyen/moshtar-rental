using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Carts;
using Moshtar.Application.Catalog;
using Moshtar.Application.Prices;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Carts;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Marketing;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Carts;

/// <summary>
/// Met een eigen, kortlevende context per oproep: de kop van elke pagina vraagt het aantal op terwijl de pagina zelf
/// de gedeelde context gebruikt.
/// </summary>
internal sealed class CartService(
    IDbContextFactory<AppDbContext> contexts, ICatalogService catalog, IReservationService reservations, ITenantContext tenantContext,
    TimeProvider clock, IPriceQuotes prices) : ICartService
{
    public async Task<CartView?> GetAsync(string? key, string culture, CancellationToken ct = default)
    {
        if (await FindAsync(key, ct) is not { } cart) return null;

        var entries = (await catalog.GetCatalogAsync(culture, ct)).ToDictionary(e => Ref(e));
        var period = cart.Period;
        var unavailable = await UnavailableItemsAsync(cart, entries, period, ct);
        var priced = await PriceAsync(cart, entries, culture, ct);
        var lines = cart.Lines.Select(l =>
        {
            var entry = entries.GetValueOrDefault(l.Entry);
            return new CartLineView(l.Entry, entry, l.Quantity, priced.UnitPrices.GetValueOrDefault(l.Entry),
                Available: entry is not null && !Occupies(l, unavailable, entry));
        }).ToList();
        return new CartView(period, lines, cart.Email, cart.WantsReminder, cart.WantsNews, priced.Discounts);
    }

    /// <summary>
    /// De prijs per stuk van elke lijn die nog op de reservatiewebsite staat en de kortingslijnen, met de prijsregels
    /// van vandaag: dezelfde berekening als bij het reserveren.
    /// </summary>
    private async Task<(Dictionary<CatalogRef, decimal> UnitPrices, IReadOnlyList<CartDiscountView> Discounts)> PriceAsync(
        Cart cart, Dictionary<CatalogRef, CatalogEntry> entries, string culture, CancellationToken ct)
    {
        var quoter = await prices.CurrentAsync(ct);
        var lines = cart.Lines.Where(l => entries.ContainsKey(l.Entry)).Select(l => entries[l.Entry].ToPriceLine(l.Quantity)).ToList();
        var result = quoter.Quote(cart.Period, lines);
        var fallback = tenantContext.Tenant?.DefaultCulture ?? culture;
        return (result.Lines.ToDictionary(l => l.Line.Entry, l => l.UnitPrice),
            result.Discounts.Select(d => new CartDiscountView(d.Rule.NameIn(culture, fallback), d.Amount)).ToList());
    }

    public async Task<int> CountAsync(string? key, CancellationToken ct = default) =>
        await FindAsync(key, ct) is { } cart ? cart.Count : 0;

    public async Task<CartChange> AddAsync(string? key, CatalogRef entry, int quantity, DateRange period, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = await contexts.CreateDbContextAsync(ct);
        var cart = await FindAsync(db, key, ct);
        var candidate = new Cart { Lines = cart?.Lines.Select(Copy).ToList() ?? [] };
        candidate.Add(entry, quantity, period, now);

        // Alleen weigeren als het artikel of pakket zelf, samen met de rest van de winkelkar, niet past: wat al in de
        // winkelkar zat en in de nieuwe huurperiode niet meer vrij is, toont de winkelkar zelf.
        // Alleen welke artikelen en pakketten bestaan telt hier, niet hun naam: de taal maakt niet uit.
        var entries = (await catalog.GetCatalogAsync("nl", ct)).ToDictionary(e => Ref(e));
        if (!entries.TryGetValue(entry, out var added))
            return new CartChange(cart?.Key, false);
        var unavailable = await UnavailableItemsAsync(candidate, entries, period, ct);
        if (Occupies(candidate.Lines.Single(l => l.Entry == entry), unavailable, added))
            return new CartChange(cart?.Key, false);

        if (cart is null)
        {
            cart = Cart.Create(NewKey(), period, now);
            db.Carts.Add(cart);
        }
        cart.Add(entry, quantity, period, now);
        await db.SaveChangesAsync(ct);
        return new CartChange(cart.Key, true);
    }

    public async Task<CartChange> SetQuantityAsync(string? key, CatalogRef entry, int quantity, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (await FindAsync(db, key, ct) is not { } cart) return new CartChange(null, false);
        cart.SetQuantity(entry, quantity, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return new CartChange(cart.Key, true);
    }

    public async Task<CartChange> ChangePeriodAsync(string? key, DateRange period, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (await FindAsync(db, key, ct) is not { } cart) return new CartChange(null, false);
        cart.ChangePeriod(period, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return new CartChange(cart.Key, true);
    }

    public async Task<CartChange> IdentifyAsync(string? key, string email, string culture, bool wantsReminder, bool wantsNews, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (string.IsNullOrEmpty(key)) return new CartChange(null, false);
        // Na elkaar met andere wijzigingen aan de toestemming en met het reserveren van deze winkelkar, zodat een
        // trager verzoek de toestemming niet terugzet naar wat het eerder las.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, key, ct);
        if (await FindAsync(db, key, ct) is not { } cart) return new CartChange(null, false);
        var (previousEmail, previouslyWantedNews) = (cart.Email, cart.WantsNews);
        var now = clock.GetUtcNow().UtcDateTime;
        cart.Identify(email, culture, wantsReminder, wantsNews, now);
        await db.SaveChangesAsync(ct);
        if (wantsNews)
            await ConsentAsync(db, cart.Email!, culture, MarketingContactSource.Cart, now, ct);
        else if (previouslyWantedNews && previousEmail is not null)
            // De klant vinkte het nieuws in deze winkelkar uit: zijn toestemming is ingetrokken. Een vinkje dat in een
            // nieuwe winkelkar gewoon uit blijft, laat een eerdere toestemming wel staan.
            await db.MarketingContacts.Where(c => c.Email == previousEmail).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return new CartChange(cart.Key, true);
    }

    public async Task<CartReservationResult> ReserveAsync(string? key, CartCustomerDetails details, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return CartReservationResult.Refused("CartGone");
        await using var db = await contexts.CreateDbContextAsync(ct);
        // Eén reservatie per winkelkar, ook als de klant twee keer op "Reserveren" klikt: de tweede wacht hier tot de
        // eerste klaar is en krijgt dan dezelfde reservatie te zien, zonder tweede reservatie of mail.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, key, ct);
        if (await db.Carts.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key && c.ReservationId != null, ct) is { } reserved)
            return await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reserved.ReservationId, ct) is { } earlier
                ? new CartReservationResult(earlier, new HashSet<Guid>(), null)
                : CartReservationResult.Refused("CartGone");
        if (await FindAsync(db, key, ct) is not { } cart || cart.Lines.Count == 0)
            return CartReservationResult.Refused("CartGone");
        if (cart.Email is null)
            return CartReservationResult.Refused("CartNoEmail");
        // Ook de reserveerflow weigert dit, maar dan zonder melding in de taal van de klant.
        if (string.IsNullOrWhiteSpace(details.Phone))
            return CartReservationResult.Refused("CartPhoneRequired");
        if (PhoneNumber.International(details.Phone) is null)
            return CartReservationResult.Refused("CartPhoneInvalid");
        // Wat niet meer op de reservatiewebsite staat, kan niet gereserveerd worden: de klant verwijdert het eerst.
        var entries = (await catalog.GetCatalogAsync(details.Culture, ct)).Select(Ref).ToHashSet();
        if (cart.Lines.Any(l => !entries.Contains(l.Entry)))
            return CartReservationResult.Refused("CartHasGoneItems");

        ReservationResult result;
        try
        {
            result = await reservations.ReserveAsync(new ReservationRequest(
                cart.Period,
                cart.Lines.Select(l => new ReservationLineRequest(l.RentalItemId, l.BundleId, l.Quantity)).ToList(),
                new ReservationCustomer(details.FirstName, details.LastName, cart.Email, details.Phone, details.Address),
                details.DeliveryMethod,
                details.Address,
                details.Notes,
                details.Culture),
                ReservationActor.Website, ct);
        }
        catch (ArgumentException)
        {
            // Bv. een huurperiode die intussen in het verleden ligt.
            return CartReservationResult.Refused("CartCannotReserve");
        }
        if (result.Reservation is not { } reservation)
            return new CartReservationResult(null, result.Shortages.Select(s => s.RentalItemId).ToHashSet(), null);

        var now = clock.GetUtcNow().UtcDateTime;
        cart.MarkReserved(reservation.Id, now);
        await db.SaveChangesAsync(CancellationToken.None);
        if (cart.WantsNews)
            await ConsentAsync(db, cart.Email, details.Culture, MarketingContactSource.Reservation, now, CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
        return new CartReservationResult(reservation, new HashSet<Guid>(), null);
    }

    public async Task<CartReminderTarget?> FindByReminderKeyAsync(string reminderKey, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reminderKey)) return null;
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Carts.AsNoTracking()
            .Where(c => c.ReminderKey == reminderKey && c.ReservedAtUtc == null)
            .Select(c => new CartReminderTarget(c.Key, c.Culture))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Bewaart de toestemming voor nieuws en acties: een nieuw marketingcontact, of een bestaand met de nieuwe taal,
    /// het nieuwe tijdstip en de nieuwe herkomst. In één statement, zodat twee winkelkarren met hetzelfde
    /// e-mailadres tegelijk geen dubbel contact geven.
    /// </summary>
    private async Task ConsentAsync(AppDbContext db, string email, string culture, MarketingContactSource source, DateTime atUtc, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "MarketingContacts" ("Id", "TenantId", "Email", "Culture", "ConsentedAtUtc", "Source")
            VALUES ({Guid.NewGuid()}, {tenantId}, {email}, {culture}, {atUtc}, {source.ToString()})
            ON CONFLICT ("TenantId", "Email") DO UPDATE
            SET "Culture" = EXCLUDED."Culture", "ConsentedAtUtc" = EXCLUDED."ConsentedAtUtc", "Source" = EXCLUDED."Source"
            WHERE "MarketingContacts"."ConsentedAtUtc" <= EXCLUDED."ConsentedAtUtc"
            """, ct);
    }

    /// <summary>Wijzigingen aan dezelfde winkelkar na elkaar, tot het einde van de transactie.</summary>
    private static Task LockAsync(AppDbContext db, string key, CancellationToken ct) =>
        db.Database.AcquireAdvisoryLockAsync("winkelkar:" + key, ct);

    /// <summary>Een nieuwe, niet te raden sleutel, voor de cookie of de link in de herinneringsmail.</summary>
    internal static string NewKey() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private async Task<Cart?> FindAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key)) return null;
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.Carts.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key && c.ReservedAtUtc == null, ct);
    }

    private static async Task<Cart?> FindAsync(AppDbContext db, string? key, CancellationToken ct) =>
        string.IsNullOrEmpty(key) ? null : await db.Carts.FirstOrDefaultAsync(c => c.Key == key && c.ReservedAtUtc == null, ct);

    /// <summary>
    /// De artikelen waarvan te weinig vrij is als alles uit de winkelkar samen gehuurd wordt, met de bestaande
    /// beschikbaarheidsberekening. Lijnen die niet meer op de reservatiewebsite staan, tellen niet mee.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> UnavailableItemsAsync(Cart cart, Dictionary<CatalogRef, CatalogEntry> entries, DateRange period, CancellationToken ct)
    {
        var lines = cart.Lines.Where(l => entries.ContainsKey(l.Entry))
            .Select(l => new ReservationLineRequest(l.RentalItemId, l.BundleId, l.Quantity)).ToList();
        if (lines.Count == 0) return new HashSet<Guid>();
        var shortages = await reservations.CheckAvailabilityAsync(period, lines, ct);
        return shortages.Select(s => s.RentalItemId).ToHashSet();
    }

    /// <summary>Gebruikt deze lijn een artikel waarvan te weinig vrij is? Voor een pakket: een van zijn artikelen.</summary>
    private static bool Occupies(CartLine line, IReadOnlySet<Guid> unavailable, CatalogEntry entry) =>
        line.RentalItemId is { } itemId ? unavailable.Contains(itemId) : entry.ItemIds.Any(unavailable.Contains);

    private static CatalogRef Ref(CatalogEntry e) => e.Kind == CatalogEntryKind.Item ? CatalogRef.Item(e.Id) : CatalogRef.Bundle(e.Id);

    private static CartLine Copy(CartLine l) => new() { RentalItemId = l.RentalItemId, BundleId = l.BundleId, Quantity = l.Quantity };

    /// <summary>32 willekeurige bytes, URL-veilig: niet te raden, ook niet door iemand die andere sleutels kent.</summary>
    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
