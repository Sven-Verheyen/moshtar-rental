using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Availability;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Reservations;

internal sealed class ReservationAlternatives(
    AppDbContext db, ITenantContext tenantContext, IReservationService reservations, IMailer mailer, IImageStore images, TimeProvider clock)
    : IReservationAlternatives
{
    /// <summary>De breedte van een foto in het back office en in de mail.</summary>
    private const int PhotoWidth = 400;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-BE");

    public async Task<IReadOnlyList<Alternative>> GetAsync(Guid reservationId, Guid rentalItemId, CancellationToken ct = default)
    {
        var tenant = Tenant;
        var found = await FindAsync(reservationId, rentalItemId, ct);
        var previous = await db.AlternativeProposals.AsNoTracking()
            .Where(p => p.ReservationId == reservationId && p.RentalItemId == rentalItemId)
            .ToDictionaryAsync(p => p.AlternativeRentalItemId, p => p.Surcharge, ct);
        return found.Alternatives
            .Select(a => new Alternative(
                a.Id,
                a.Translations.For(tenant.DefaultCulture, tenant.DefaultCulture)?.Name ?? a.Slug,
                PhotoUrl(a),
                (a.Pricing.PriceFor(found.Reservation.Period.Days) - found.Item.Pricing.PriceFor(found.Reservation.Period.Days)) * found.Quantity,
                previous.GetValueOrDefault(a.Id)))
            .ToList();
    }

    public async Task MailAsync(Guid reservationId, Guid rentalItemId, IReadOnlyList<ChosenAlternative> chosen, ReservationActor actor, CancellationToken ct = default)
    {
        if (chosen.Count == 0)
            throw new ArgumentException("Vink minstens één alternatief aan.");
        if (chosen.Any(c => c.Surcharge < 0))
            throw new ArgumentException("Een meerprijs kan niet negatief zijn.");
        if (chosen.Select(c => c.RentalItemId).Distinct().Count() != chosen.Count)
            throw new ArgumentException("Elk alternatief kan maar één keer in de mail.");

        var tenant = Tenant;
        // In het back office leeft deze service zo lang als het scherm open staat: altijd vers inlezen.
        db.ChangeTracker.Clear();
        var found = await FindAsync(reservationId, rentalItemId, ct);
        var free = found.Alternatives.ToDictionary(a => a.Id);
        if (chosen.FirstOrDefault(c => !free.ContainsKey(c.RentalItemId)) is not null)
            throw new ArgumentException("Een gekozen artikel is geen vrij alternatief meer. Laad de tekorten opnieuw.");

        var reservation = found.Reservation;
        var language = ReservationConfirmation.LanguageOf(reservation, tenant);
        var host = await db.TenantHosts.AsNoTracking().Where(h => h.TenantId == tenant.Id)
            .OrderByDescending(h => h.IsPrimary).Select(h => h.Hostname).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Je hebt nog geen domein: de links naar de artikelpagina's kunnen nergens heen.");
        var surcharges = chosen.ToDictionary(c => c.RentalItemId, c => c.Surcharge is > 0 ? c.Surcharge : null);
        var mailed = chosen.Select(c => free[c.RentalItemId]).Select(a => new MailedAlternative(
                NameIn(a.Translations, a.Slug, language, tenant),
                PhotoUrl(a),
                surcharges[a.Id],
                $"https://{host}{RentalPath.For(a.Slug, language, tenant.DefaultCulture)}"))
            .ToList();

        var customization = await db.MailTemplateCustomizations.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Kind == MailKind.ReservationAlternatives && t.Culture == language, ct);
        var template = customization?.Text ?? MailKinds.ReservationAlternatives.Standard(language);
        var unavailable = NameIn(found.Item.Translations, found.Item.Slug, language, tenant);
        await mailer.SendAsync(AlternativesMail.Create(reservation, tenant, images.LogoUrl(tenant), template, unavailable, mailed), ct);

        // De mail is weg: het tijdstip, de historiek en de meerprijzen gaan samen in één transactie.
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
        await db.AlternativeProposals.Where(p => p.ReservationId == reservationId && p.RentalItemId == rentalItemId).ExecuteDeleteAsync(CancellationToken.None);
        db.AlternativeProposals.AddRange(chosen.Select((c, i) => new AlternativeProposal
        {
            SortOrder = i,
            ReservationId = reservationId,
            RentalItemId = rentalItemId,
            AlternativeRentalItemId = c.RentalItemId,
            Surcharge = surcharges[c.RentalItemId],
            MailedAtUtc = now,
        }));
        var tracked = await db.Reservations.FirstAsync(r => r.Id == reservationId, CancellationToken.None);
        var note = $"{NameIn(found.Item.Translations, found.Item.Slug, tenant.DefaultCulture, tenant)}: " + string.Join(", ", chosen.Select(c =>
            $"{NameIn(free[c.RentalItemId].Translations, free[c.RentalItemId].Slug, tenant.DefaultCulture, tenant)} " +
            (surcharges[c.RentalItemId] is { } s ? $"(meerprijs {s.ToString("C", Dutch)})" : "(zonder meerprijs)")));
        tracked.RecordAlternativesMailed(actor, now, note);
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
    }

    public async Task<IReadOnlyList<ProposedAlternative>> GetProposedAsync(Guid reservationId, CancellationToken ct = default) =>
        await db.AlternativeProposals.AsNoTracking()
            .Where(p => p.ReservationId == reservationId)
            .OrderBy(p => p.MailedAtUtc).ThenBy(p => p.SortOrder)
            .Select(p => new ProposedAlternative(p.RentalItemId, p.AlternativeRentalItemId, p.Surcharge, p.MailedAtUtc))
            .ToListAsync(ct);

    private sealed record Found(Reservation Reservation, RentalItem Item, int Quantity, IReadOnlyList<RentalItem> Alternatives);

    /// <summary>
    /// De reservatie, het artikel, hoeveel ze ervan nodig heeft (ook via een pakket) en de alternatieven: de artikelen uit
    /// dezelfde categorie die aan staan en in de volledige huurperiode (met bufferdagen) vrij zijn voor dat aantal.
    /// </summary>
    private async Task<Found> FindAsync(Guid reservationId, Guid rentalItemId, CancellationToken ct)
    {
        var reservation = await db.Reservations.AsNoTracking().Include(r => r.Customer).FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new ArgumentException("Reservatie niet gevonden.");
        var bundleIds = reservation.Lines.Select(l => l.BundleId).OfType<Guid>().Distinct().ToList();
        var bundles = await db.Bundles.AsNoTracking().Where(b => bundleIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var quantity = DemandExpander.Expand(reservation.Lines, reservation.Period, bundles)
            .Where(d => d.RentalItemId == rentalItemId).Sum(d => d.Quantity);
        if (quantity == 0)
            throw new ArgumentException("Dit artikel zit niet in de reservatie.");
        var item = await db.RentalItems.AsNoTracking().FirstAsync(i => i.Id == rentalItemId, ct);
        if (item.CategoryId is not { } categoryId || !reservation.OccupiesStock)
            return new Found(reservation, item, quantity, []);

        var candidates = await db.RentalItems.AsNoTracking()
            .Where(i => i.CategoryId == categoryId && i.IsActive && i.Id != rentalItemId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Slug)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return new Found(reservation, item, quantity, []);

        // Elk alternatief is een ander artikel: samen nagaan geeft per artikel of het vrij is.
        var shortages = await reservations.CheckAvailabilityAsync(
            reservation.Period, candidates.Select(c => new ReservationLineRequest(c.Id, null, quantity)).ToList(), ct);
        var taken = shortages.Select(s => s.RentalItemId).ToHashSet();
        return new Found(reservation, item, quantity, candidates.Where(c => !taken.Contains(c.Id)).ToList());
    }

    private string? PhotoUrl(RentalItem item) =>
        item.Photos.OrderBy(p => p.SortOrder).FirstOrDefault() is { } photo ? images.PublicUrl(photo.Key, PhotoWidth) : null;

    private static string NameIn(IEnumerable<Translation> translations, string slug, string language, Tenant tenant) =>
        translations.For(language, tenant.DefaultCulture)?.Name ?? slug;

    private Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
