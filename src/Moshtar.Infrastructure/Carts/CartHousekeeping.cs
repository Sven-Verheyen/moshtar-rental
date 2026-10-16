using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Carts;
using Moshtar.Application.Images;
using Moshtar.Application.Mail;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Carts;

internal sealed class CartHousekeeping(
    AppDbContext db, ICartService carts, ITenantContext tenantContext, IMailer mailer, IImageStore images, ILogger<CartHousekeeping> logger) : ICartHousekeeping
{
    public async Task<CartHousekeepingResult> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");

        // Eerst opruimen: een winkelkar van 7 dagen oud krijgt geen herinnering meer.
        var expiredBefore = nowUtc - Cart.Lifetime;
        var deleted = await db.Carts.Where(c => c.ReservedAtUtc == null && c.UpdatedAtUtc <= expiredBefore).ExecuteDeleteAsync(ct);

        var dueBefore = nowUtc - Cart.ReminderAfter;
        var due = await db.Carts.AsNoTracking()
            .Where(c => c.WantsReminder && c.Email != null && c.ReservedAtUtc == null && c.ReminderKey == null && c.UpdatedAtUtc <= dueBefore)
            .ToListAsync(ct);
        if (due.Count == 0) return new CartHousekeepingResult(0, deleted);

        var host = await db.PrimaryHostAsync(tenant.Id, ct);
        if (host is null)
        {
            logger.LogWarning("Verhuurder {Tenant} heeft geen domein: geen herinneringsmails voor winkelkarren.", tenant.Slug);
            return new CartHousekeepingResult(0, deleted);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TimeZoneInfo.Local));
        var sent = 0;
        foreach (var cart in due.Where(c => c.IsReminderDue(nowUtc) && c.Period.Start >= today))
        {
            var language = CartReminder.LanguageOf(cart.Culture, tenant);
            // Geen herinnering als niets uit de winkelkar nog vrij is: daar kan de klant niets mee.
            if (await carts.GetAsync(cart.Key, language, ct) is not { } view || !view.Lines.Any(l => l.Entry is not null && l.Available))
                continue;

            // Eerst de winkelkar opeisen met de sleutel, dan mailen: zo vertrekt er nooit een tweede herinnering, ook
            // niet als het onderhoud twee keer tegelijk loopt of de mail mislukt. Liever één gemiste herinnering dan twee.
            // ReminderSentAtUtc komt pas als de mail echt vertrok, zodat het back office geen mislukte toont als verstuurd.
            var reminderKey = CartService.NewKey();
            var claimed = await db.Carts
                .Where(c => c.Id == cart.Id && c.ReminderKey == null && c.ReservedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReminderKey, reminderKey), ct);
            if (claimed == 0) continue;

            try
            {
                var customization = await db.MailTemplateCustomizations.AsNoTracking()
                    .SingleOrDefaultAsync(t => t.Kind == MailKind.CartReminder && t.Culture == language, ct);
                var template = customization?.Text ?? MailKinds.CartReminder.Standard(language);
                var link = $"https://{host}{CartReminderPath.For(reminderKey)}";
                await mailer.SendAsync(CartReminder.Create(view, language, tenant, images.LogoUrl(tenant), template, link), CancellationToken.None);
                sent++;
                await db.Carts.Where(c => c.Id == cart.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReminderSentAtUtc, nowUtc), CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Herinneringsmail voor een winkelkar van verhuurder {Tenant} kon niet vertrekken.", tenant.Slug);
            }
        }
        return new CartHousekeepingResult(sent, deleted);
    }
}
