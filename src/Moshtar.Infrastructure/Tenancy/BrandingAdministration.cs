using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Tenancy;

internal sealed class BrandingAdministration(
    AppDbContext db, ITenantContext tenantContext, ITenantStore store, IImageStore images, ILogger<BrandingAdministration> logger)
    : IBrandingAdministration
{
    public async Task<BrandingSettings> GetAsync(CancellationToken ct = default)
    {
        var id = Tenant.Id;
        var saved = await db.Tenants.AsNoTracking().Where(t => t.Id == id)
            .Select(t => new { t.LogoImageKey, t.PrimaryColor, t.HeroImageKey }).SingleAsync(ct);
        return new BrandingSettings(
            saved.LogoImageKey is { } logo ? images.PublicUrl(logo, BrandingImages.LogoWidth) : null,
            saved.PrimaryColor,
            saved.HeroImageKey is { } hero ? images.PublicUrl(hero, BrandingImages.HeroImageWidth) : null);
    }

    public async Task SaveColorAsync(string? color, CancellationToken ct = default)
    {
        if (Branding.Problems(color) is { Count: > 0 } problems)
            throw new BrandingException(problems);

        var tenant = await TrackedTenantAsync(ct);
        tenant.PrimaryColor = Branding.Color(color);
        await SaveAsync(tenant, ct);
    }

    public async Task UploadAsync(BrandingImage image, Stream content, CancellationToken ct = default)
    {
        var key = await images.UploadAsync(content, ct);
        await ReplaceAsync(image, key, ct);
    }

    public Task RemoveAsync(BrandingImage image, CancellationToken ct = default) => ReplaceAsync(image, null, ct);

    public string PreviewHeader(BrandingSettings settings) => MailHeader.Html(Tenant.Name, settings.LogoUrl, settings.PrimaryColor);

    /// <summary>Zet de nieuwe afbeelding (of geen) in de huisstijl en ruimt de vorige op uit de opslag.</summary>
    private async Task ReplaceAsync(BrandingImage image, string? key, CancellationToken ct)
    {
        var tenant = await TrackedTenantAsync(ct);
        var previous = image == BrandingImage.Logo ? tenant.LogoImageKey : tenant.HeroImageKey;
        if (image == BrandingImage.Logo) tenant.LogoImageKey = key;
        else tenant.HeroImageKey = key;
        await SaveAsync(tenant, ct);

        if (previous is null || previous == key) return;
        try
        {
            await images.DeleteAsync(previous, CancellationToken.None);
        }
        catch (Exception e)
        {
            // De huisstijl is al bewaard; een afbeelding die blijft staan, is enkel verloren opslag.
            logger.LogError(e, "Vorige afbeelding {Key} van verhuurder {Tenant} kon niet opgeruimd worden.", previous, tenant.Slug);
        }
    }

    private async Task<Tenant> TrackedTenantAsync(CancellationToken ct)
    {
        var id = Tenant.Id;
        return await db.Tenants.SingleAsync(t => t.Id == id, ct);
    }

    private async Task SaveAsync(Tenant tenant, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        // Ook wat in dit scherm (dezelfde Blazor-circuit) en in de cache van verhuurders zit, gebruikt meteen de nieuwe huisstijl.
        Tenant.LogoImageKey = tenant.LogoImageKey;
        Tenant.PrimaryColor = tenant.PrimaryColor;
        Tenant.HeroImageKey = tenant.HeroImageKey;
        store.Forget(tenant.Id);
    }

    private Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
