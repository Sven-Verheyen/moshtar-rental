using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Tenancy;

internal sealed class BrandingAdministration(AppDbContext db, ITenantContext tenantContext, ITenantStore store) : IBrandingAdministration
{
    public async Task<BrandingSettings> GetAsync(CancellationToken ct = default)
    {
        var id = Tenant.Id;
        return await db.Tenants.AsNoTracking().Where(t => t.Id == id)
            .Select(t => new BrandingSettings(t.LogoUrl, t.PrimaryColor)).SingleAsync(ct);
    }

    public async Task SaveAsync(BrandingSettings settings, CancellationToken ct = default)
    {
        if (Branding.Problems(settings.LogoUrl, settings.PrimaryColor) is { Count: > 0 } problems)
            throw new BrandingException(problems);

        var id = Tenant.Id;
        var tenant = await db.Tenants.SingleAsync(t => t.Id == id, ct);
        tenant.LogoUrl = Branding.LogoUrl(settings.LogoUrl);
        tenant.PrimaryColor = Branding.Color(settings.PrimaryColor);
        await db.SaveChangesAsync(ct);
        // Anders gebruiken mails nog even de oude huisstijl uit de cache.
        store.Forget(id);
    }

    public string PreviewHeader(BrandingSettings settings) => MailHeader.Html(Tenant.Name, settings.LogoUrl, settings.PrimaryColor);

    private Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
