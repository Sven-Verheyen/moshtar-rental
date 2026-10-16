using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Images;
using Moshtar.Application.Mail;
using Moshtar.Application.Maps;
using Moshtar.Infrastructure.Maps;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Prices;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Prices;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>
/// Reserveren, de status wijzigen, artikelen vervangen en de beschikbaarheid nagaan. Elke bewerking heeft haar eigen
/// databankcontext: in het back office leeft deze service zo lang als het scherm open staat, en meerdere onderdelen van
/// één scherm (zoals de alternatieven per reservatie in Tekorten) mogen ze tegelijk gebruiken.
/// </summary>
internal sealed class ReservationService(
    IDbContextFactory<AppDbContext> contexts, ITenantContext tenantContext, TimeProvider clock, IMailer mailer, IImageStore images,
    IMapService maps, ILogger<ReservationService> logger) : IReservationService
{
    public async Task<IReadOnlyList<StockShortage>> CheckAvailabilityAsync(
        DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var (shortages, _, _) = await EvaluateAsync(db, period, lines, ct);
        return shortages;
    }

    public async Task<ReservationResult> ReserveAsync(ReservationRequest request, ReservationActor actor, CancellationToken ct = default)
    {
        Validate(request);
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");
        var deliveryAddress = request.DeliveryMethod == DeliveryMethod.Delivery ? request.DeliveryAddress ?? request.Customer.Address : null;
        // Voor de lock: de kaartdienst kan even op zich laten wachten, en andere reservaties moeten daar niet op wachten.
        var deliveryPoint = deliveryAddress is null ? null : await maps.TryFindAsync(deliveryAddress, logger, ct);

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTenantAsync(db, tenant, ct);

        var (shortages, items, bundles) = await EvaluateAsync(db, request.Period, request.Lines, ct);
        if (shortages.Count > 0)
            return new ReservationResult(null, shortages);

        var customer = await UpsertCustomerAsync(db, request, actor, ct);
        // De prijsregels van vandaag, binnen dezelfde transactie: dezelfde berekening als de winkelkar.
        var quoter = await PriceQuotes.CurrentAsync(db, clock, ct);
        var priced = quoter.Quote(request.Period, request.Lines.Select(l => PriceLineOf(l, items, bundles)).ToList());
        var reservation = new Reservation
        {
            Number = await NextNumberAsync(db, ct),
            Customer = customer,
            StartDate = request.Period.Start,
            EndDate = request.Period.End,
            Status = ReservationStatus.Confirmed,
            DeliveryMethod = request.DeliveryMethod,
            DeliveryAddress = deliveryAddress,
            DeliveryPoint = deliveryPoint,
            Notes = request.Notes,
            Culture = request.Culture,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Lines = request.Lines.Select((l, i) => CreateLine(l, items, bundles, priced.Lines[i].UnitPrice, request.Culture, tenant.DefaultCulture))
                // Elke korting als eigen lijn zonder artikel of pakket, met de naam van de regel in de taal van de reservatie.
                .Concat(priced.Discounts.Select(d => new ReservationLine
                {
                    Quantity = 1, Description = d.Rule.NameIn(request.Culture, tenant.DefaultCulture), UnitPrice = -d.Amount,
                }))
                .ToList(),
        };
        reservation.TotalPrice = reservation.Lines.Sum(l => l.LineTotal);
        reservation.RecordCreated(actor, reservation.CreatedAtUtc);

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await SendConfirmationAsync(db, reservation, tenant);
        return new ReservationResult(reservation, []);
    }

    /// <summary>
    /// Bevestigt de reservatie per mail aan de klant. Pas na de commit, en een mislukte mail maakt de reservatie
    /// niet ongedaan: die staat vast, de verhuurder kan de klant nog altijd zelf contacteren.
    /// Daarom ook geen CancellationToken: een afgebroken request mag de mail niet tegenhouden,
    /// en ReserveAsync mag niet mislukken voor een reservatie die al bewaard is.
    /// </summary>
    private async Task SendConfirmationAsync(AppDbContext db, Reservation reservation, Tenant tenant)
    {
        try
        {
            var language = ReservationConfirmation.LanguageOf(reservation, tenant);
            var customization = await db.MailTemplateCustomizations.AsNoTracking().SingleOrDefaultAsync(
                t => t.Kind == MailKind.ReservationConfirmation && t.Culture == language, CancellationToken.None);
            var template = customization?.Text ?? MailKinds.ReservationConfirmation.Standard(language);
            await mailer.SendAsync(ReservationConfirmation.Create(reservation, tenant, images.LogoUrl(tenant), template), CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Bevestigingsmail voor reservatie {Number} van verhuurder {Tenant} kon niet vertrekken.", reservation.Number, tenant.Slug);
        }
    }

    public async Task<Reservation> ChangeStatusAsync(Guid reservationId, ReservationStatus status, ReservationActor actor, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new InvalidOperationException("Reservatie niet gevonden.");
        reservation.ChangeStatus(status, actor, clock.GetUtcNow().UtcDateTime);
        // De nieuwe status en de gebeurtenis in de historiek gaan samen in één SaveChanges, dus één transactie.
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> CorrectDeliveryAddressAsync(Guid reservationId, Address address, ReservationActor actor, CancellationToken ct = default)
    {
        var corrected = new Address
        {
            Street = address.Street.Trim(), PostalCode = address.PostalCode.Trim(), City = address.City.Trim(),
            Country = string.IsNullOrWhiteSpace(address.Country) ? "BE" : address.Country.Trim(),
        };
        if (corrected.Street.Length == 0 || corrected.PostalCode.Length == 0 || corrected.City.Length == 0)
            throw new ArgumentException("Vul de straat met huisnummer, de postcode en de gemeente in.");
        var point = await maps.TryFindAsync(corrected, logger, ct);

        await using var db = await contexts.CreateDbContextAsync(ct);
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new InvalidOperationException("Reservatie niet gevonden.");
        reservation.CorrectDeliveryAddress(corrected, point, actor, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> ConfirmDeliveryPointAsync(Guid reservationId, GeoPoint point, ReservationActor actor, CancellationToken ct = default)
    {
        if (!GeoPoint.IsValid(point.Latitude, point.Longitude))
            throw new ArgumentException("Dit is geen punt op de kaart.");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new InvalidOperationException("Reservatie niet gevonden.");
        reservation.ConfirmDeliveryPoint(point, actor, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return reservation;
    }

    public async Task<Reservation> ReplaceItemAsync(Guid reservationId, Guid rentalItemId, Guid replacementRentalItemId, decimal? surcharge,
        ReservationActor actor, CancellationToken ct = default)
    {
        if (surcharge < 0)
            throw new ArgumentException("Een meerprijs kan niet negatief zijn.");
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTenantAsync(db, tenant, ct);

        var reservation = await db.Reservations.Include(r => r.Customer).FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new ArgumentException("Reservatie niet gevonden.");
        var bundleIds = reservation.Lines.Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var used = DemandExpander.Expand(reservation, bundles).Where(d => d.Demand.RentalItemId == rentalItemId).ToList();
        var quantity = DemandExpander.QuantityOf(reservation, rentalItemId, bundles);
        if (quantity == 0)
            throw new ArgumentException("Dit artikel zit niet in de reservatie.");

        var item = await db.RentalItems.AsNoTracking().FirstAsync(i => i.Id == rentalItemId, ct);
        var replacement = await db.RentalItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == replacementRentalItemId, ct);
        if (replacement is null || replacement.Id == item.Id || !replacement.IsActive || item.CategoryId is null || replacement.CategoryId != item.CategoryId)
            throw new ArgumentException("Vervang enkel door een ander artikel uit dezelfde categorie dat aan staat.");

        // Vrij in de volledige huurperiode, voor alles wat de reservatie van het artikel gebruikt (los en in pakketten).
        var (shortages, _, _) = await EvaluateAsync(db, reservation.Period,
            [new ReservationLineRequest(replacement.Id, null, quantity)], ct);
        string DefaultName(RentalItem i) => i.Translations.For(tenant.DefaultCulture, tenant.DefaultCulture)?.Name ?? i.Slug;
        if (shortages.Count > 0)
            throw new ArgumentException($"{DefaultName(replacement)} is niet vrij in de volledige huurperiode van reservatie {reservation.Number}.");

        // Op de reservatie en in de mail in de taal van de klant, in de historiek in die van de verhuurder.
        string Name(RentalItem i) => i.Translations.For(reservation.Culture, tenant.DefaultCulture)?.Name ?? i.Slug;
        var amount = surcharge is > 0 ? surcharge : null;
        var inBundles = used.Select(d => d.BundleId).OfType<Guid>().Distinct()
            .Select(id => bundles[id].Translations.For(tenant.DefaultCulture, tenant.DefaultCulture)?.Name ?? bundles[id].Slug).ToList();
        var note = $"{DefaultName(item)} vervangen door {DefaultName(replacement)}"
                   + (inBundles.Count > 0 ? $" in {string.Join(", ", inBundles)}" : "")
                   + " " + SurchargeNote.For(amount);
        reservation.ReplaceItem(
            new ItemReplacement(item.Id, Name(item), replacement.Id, Name(replacement), amount,
                ReservationConfirmation.SurchargeDescription(reservation, tenant, Name(replacement), Name(item))),
            bundles, actor, clock.GetUtcNow().UtcDateTime, note);

        // De voorgestelde alternatieven voor dit artikel zijn afgehandeld.
        await db.AlternativeProposals.Where(p => p.ReservationId == reservationId && p.RentalItemId == rentalItemId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await SendConfirmationAsync(db, reservation, tenant);
        return reservation;
    }

    /// <summary>Reservaties per tenant na elkaar afhandelen, tot het einde van de transactie.</summary>
    private static Task LockTenantAsync(AppDbContext db, Tenant tenant, CancellationToken ct) =>
        db.Database.AcquireAdvisoryLockAsync(tenant.Id.ToString(), ct);

    public async Task<IReadOnlyList<ReservationHistoryEntry>> GetHistoryAsync(Guid reservationId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await (from e in db.ReservationEvents.AsNoTracking()
               where e.ReservationId == reservationId
               // Ook uitgeschakelde gebruikers blijven met hun e-mailadres zichtbaar.
               join u in db.Users on e.UserId equals u.Id into users
               from u in users.DefaultIfEmpty()
               orderby e.OccurredAtUtc descending, e.Kind descending
               select new ReservationHistoryEntry(e.OccurredAtUtc, e.Kind, e.Status, u.Email, e.Note, e.Automatic))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<Guid>> FindUnavailableAsync(DateRange period, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");
        await using var db = await contexts.CreateDbContextAsync(ct);
        var (reserved, blocked) = await OccupationAsync(db, period, tenant, ct);
        var items = await db.RentalItems.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
        var bundles = await db.Bundles.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        var stock = items.ToDictionary(i => i.Id, i => i.Stock);

        bool Unavailable(IEnumerable<StockDemand> requested) =>
            AvailabilityCalculator.FindShortages(stock, reserved, blocked, requested.ToList(), tenant.BufferDaysBefore, tenant.BufferDaysAfter).Count > 0;

        var unavailable = items.Where(i => Unavailable([new StockDemand(i.Id, 1, period)])).Select(i => i.Id).ToHashSet();
        // Een pakket met een inactief artikel is niet te reserveren: dat artikel heeft geen voorraad in deze berekening.
        unavailable.UnionWith(bundles
            .Where(b => Unavailable(b.Items.Select(x => new StockDemand(x.RentalItemId, x.Quantity, period))))
            .Select(b => b.Id));
        return unavailable;
    }

    private async Task<(IReadOnlyList<StockShortage> Shortages, Dictionary<Guid, RentalItem> Items, Dictionary<Guid, Bundle> Bundles)> EvaluateAsync(
        AppDbContext db, DateRange period, IReadOnlyList<ReservationLineRequest> lines, CancellationToken ct)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen tenant gekend.");
        var (reserved, blocked) = await OccupationAsync(db, period, tenant, ct);

        var bundleIds = lines.Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var requestLines = lines.Select(l => new ReservationLine { RentalItemId = l.RentalItemId, BundleId = l.BundleId, Quantity = l.Quantity }).ToList();
        if (requestLines.Any(l => l.BundleId is { } id && (!bundles.TryGetValue(id, out var b) || !b.IsActive)))
            throw new ArgumentException("Onbekend of inactief pakket.");
        var requested = DemandExpander.Expand(requestLines, period, bundles).ToList();

        var itemIds = requested.Select(d => d.RentalItemId).Distinct().ToList();
        var items = await db.RentalItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        if (lines.Any(l => l.RentalItemId is { } id && (!items.TryGetValue(id, out var i) || !i.IsActive)))
            throw new ArgumentException("Onbekend of inactief artikel.");

        // Een uitgezet artikel in een pakket is niet meer te reserveren: het heeft geen voorraad voor nieuwe reservaties.
        var stock = items.ToDictionary(kv => kv.Key, kv => kv.Value.IsActive ? kv.Value.Stock : 0);
        var shortages = AvailabilityCalculator.FindShortages(stock, reserved, blocked, requested, tenant.BufferDaysBefore, tenant.BufferDaysAfter);
        return (shortages, items, bundles);
    }

    /// <summary>De bestaande bezetting (reservaties en blokkeringen) die een vraag in deze periode kan raken.</summary>
    private async Task<(List<StockDemand> Reserved, List<StockDemand> Blocked)> OccupationAsync(AppDbContext db, DateRange period, Tenant tenant, CancellationToken ct)
    {
        var before = tenant.BufferDaysBefore;
        var after = tenant.BufferDaysAfter;

        // Bestaande reservaties waarvan de periode (met buffer) de gevraagde periode (met buffer) kan raken.
        var windowStart = period.Start.AddDays(-(before + after));
        var windowEnd = period.End.AddDays(before + after);
        var existing = await db.Reservations.AsNoTracking()
            .Where(r => Reservation.StockOccupyingStatuses.Contains(r.Status) && r.StartDate <= windowEnd && r.EndDate >= windowStart)
            .ToListAsync(ct);
        var blockouts = await db.Blockouts.AsNoTracking()
            .Where(b => b.StartDate <= period.End.AddDays(after) && (b.EndDate == null || b.EndDate >= period.Start.AddDays(-before)))
            .ToListAsync(ct);

        var bundleIds = existing.SelectMany(r => r.Lines).Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var reserved = existing.SelectMany(r => DemandExpander.Expand(r.Lines, r.Period, bundles)).ToList();
        var blocked = blockouts.Select(b => b.Demand).ToList();
        return (reserved, blocked);
    }

    /// <summary>Een gevraagde lijn als lijn voor de prijsberekening: een artikel met zijn categorie, of een pakket.</summary>
    private static PriceLine PriceLineOf(ReservationLineRequest l, Dictionary<Guid, RentalItem> items, Dictionary<Guid, Bundle> bundles) =>
        l.RentalItemId is { } itemId
            ? new PriceLine(CatalogRef.Item(itemId), items[itemId].CategoryId, items[itemId].Pricing, l.Quantity)
            : new PriceLine(CatalogRef.Bundle(l.BundleId!.Value), null, bundles[l.BundleId.Value].Pricing, l.Quantity);

    private static ReservationLine CreateLine(ReservationLineRequest l, Dictionary<Guid, RentalItem> items, Dictionary<Guid, Bundle> bundles,
        decimal unitPrice, string culture, string fallbackCulture)
    {
        if (l.RentalItemId is { } itemId)
        {
            var item = items[itemId];
            return new ReservationLine
            {
                RentalItemId = itemId,
                Quantity = l.Quantity,
                Description = item.Translations.For(culture, fallbackCulture)?.Name ?? item.Slug,
                UnitPrice = unitPrice,
            };
        }
        var bundle = bundles[l.BundleId!.Value];
        return new ReservationLine
        {
            BundleId = bundle.Id,
            Quantity = l.Quantity,
            Description = bundle.Translations.For(culture, fallbackCulture)?.Name ?? bundle.Slug,
            UnitPrice = unitPrice,
        };
    }

    /// <summary>
    /// Wie reserveert, wordt klant, of zijn klantfiche neemt de laatst ingevulde gegevens over. De interne notitie blijft
    /// altijd staan. Aangemaakt of gewijzigd door de gebruiker die reserveerde, of "via website".
    /// </summary>
    private async Task<Customer> UpsertCustomerAsync(AppDbContext db, ReservationRequest request, ReservationActor actor, CancellationToken ct)
    {
        var email = Customer.NormalizeEmail(request.Customer.Email);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer is null)
        {
            customer = new Customer { Email = email };
            db.Customers.Add(customer);
        }
        customer.FirstName = request.Customer.FirstName;
        customer.LastName = request.Customer.LastName;
        customer.Phone = PhoneNumber.International(request.Customer.Phone);
        customer.Address = request.Customer.Address;
        customer.PreferredCulture = request.Culture;
        customer.RecordChange(actor.UserId, clock.GetUtcNow().UtcDateTime);
        return customer;
    }

    /// <summary>Volgnummer per jaar, bv. 2026-0042. Veilig omdat we binnen de tenant-lock zitten.</summary>
    private async Task<string> NextNumberAsync(AppDbContext db, CancellationToken ct)
    {
        var year = clock.GetUtcNow().Year;
        var prefix = $"{year}-";
        var count = await db.Reservations.CountAsync(r => r.Number.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:0000}";
    }

    private void Validate(ReservationRequest request)
    {
        if (request.Lines.Count == 0)
            throw new ArgumentException("Een reservatie heeft minstens één artikel of pakket nodig.");
        if (request.Lines.Any(l => l.Quantity < 1 || (l.RentalItemId is null) == (l.BundleId is null)))
            throw new ArgumentException("Elke lijn moet precies één artikel of pakket bevatten met aantal ≥ 1.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (request.Period.Start < today)
            throw new ArgumentException("De huurperiode mag niet in het verleden liggen.");
        // Ook hier, en niet alleen op de reservatiewebsite: een formulier kan andere datums posten dan het toonde.
        if (RentalPeriodLimits.IsTooLong(request.Period))
            throw new ArgumentException($"Een huurperiode is hoogstens {RentalPeriodLimits.MaxDays} dagen.");
        if (RentalPeriodLimits.IsTooFarAhead(request.Period.Start, today))
            throw new ArgumentException($"Je kan hoogstens {RentalPeriodLimits.MaxYearsAhead} jaar vooruit reserveren.");
        if (string.IsNullOrWhiteSpace(request.Customer.Email))
            throw new ArgumentException("E-mailadres is verplicht.");
        // Verplicht voor elke nieuwe reservatie, zodat de verhuurder de klant kan bellen of WhatsAppen. Bestaande
        // reservaties en klanten zonder nummer blijven geldig.
        if (string.IsNullOrWhiteSpace(request.Customer.Phone))
            throw new ArgumentException("Het telefoonnummer is verplicht.");
        if (PhoneNumber.International(request.Customer.Phone) is null)
            throw new ArgumentException("Dit is geen geldig telefoonnummer, bv. 0470 12 34 56 of +32 470 12 34 56.");
    }
}
