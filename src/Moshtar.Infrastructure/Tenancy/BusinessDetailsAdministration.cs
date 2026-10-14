using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Tenancy;

internal sealed class BusinessDetailsAdministration(AppDbContext db, ITenantContext tenantContext, ITenantStore store) : IBusinessDetailsAdministration
{
    public async Task<BusinessDetailsSettings> GetAsync(CancellationToken ct = default)
    {
        var id = Tenant.Id;
        var t = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == id, ct);
        return new BusinessDetailsSettings
        {
            Street = t.Street, PostalCode = t.PostalCode, City = t.City,
            Email = t.ContactEmail, Phone = t.ContactPhone, WhatsAppPhone = t.WhatsAppPhone, VatNumber = t.VatNumber,
            FacebookUrl = t.FacebookUrl, InstagramUrl = t.InstagramUrl, TikTokUrl = t.TikTokUrl, GoogleBusinessUrl = t.GoogleBusinessUrl,
            GoogleAnalyticsId = t.GoogleAnalyticsId,
            ServiceArea = t.ServiceArea,
            OffersPickup = t.OffersPickup,
            Rounds = t.RoundSettings,
        };
    }

    public async Task SaveAsync(BusinessDetailsSettings s, CancellationToken ct = default)
    {
        var problems = BusinessDetails.Problems(s.Email, s.Phone, s.WhatsAppPhone, s.FacebookUrl, s.InstagramUrl, s.TikTokUrl, s.GoogleBusinessUrl)
            .Concat(TooLong(s))
            .Concat(BusinessDetails.GoogleAnalyticsProblem(s.GoogleAnalyticsId) is { } analytics ? [analytics] : [])
            .Concat(s.Rounds.Problems())
            .ToList();
        if (problems.Count > 0)
            throw new BusinessDetailsException(problems);

        var id = Tenant.Id;
        var tenant = await db.Tenants.SingleAsync(t => t.Id == id, ct);
        Apply(tenant, s);
        await db.SaveChangesAsync(ct);
        // Ook wat in dit scherm (dezelfde Blazor-circuit) en in de cache van verhuurders zit, toont meteen de nieuwe gegevens.
        Apply(Tenant, s);
        store.Forget(id);
    }

    private static void Apply(Tenant tenant, BusinessDetailsSettings s)
    {
        // Een ander adres is een ander punt: de planning zoekt het opnieuw op bij het openen van een dag.
        if ((tenant.Street, tenant.PostalCode, tenant.City) != (Text(s.Street), Text(s.PostalCode), Text(s.City)))
            tenant.AddressPoint = null;
        tenant.Street = Text(s.Street);
        tenant.PostalCode = Text(s.PostalCode);
        tenant.City = Text(s.City);
        tenant.ContactEmail = BusinessDetails.Email(s.Email);
        tenant.ContactPhone = BusinessDetails.Phone(s.Phone);
        tenant.WhatsAppPhone = BusinessDetails.Phone(s.WhatsAppPhone);
        tenant.VatNumber = Text(s.VatNumber);
        tenant.FacebookUrl = BusinessDetails.Link(s.FacebookUrl);
        tenant.InstagramUrl = BusinessDetails.Link(s.InstagramUrl);
        tenant.TikTokUrl = BusinessDetails.Link(s.TikTokUrl);
        tenant.GoogleBusinessUrl = BusinessDetails.Link(s.GoogleBusinessUrl);
        tenant.GoogleAnalyticsId = BusinessDetails.GoogleAnalyticsId(s.GoogleAnalyticsId);
        tenant.ServiceArea = BusinessDetails.ServiceArea(string.Join('\n', s.ServiceArea)).ToList();
        tenant.OffersPickup = s.OffersPickup;
        tenant.RoundSettings = s.Rounds;
    }

    /// <summary>Velden die langer zijn dan de database toelaat (zie TenantConfiguration).</summary>
    private static IEnumerable<string> TooLong(BusinessDetailsSettings s) =>
        new (string Field, string? Value, int Max)[]
        {
            ("De straat", s.Street, 200), ("De postcode", s.PostalCode, 20), ("De gemeente", s.City, 100),
            ("Het e-mailadres", s.Email, 254), ("Het telefoonnummer", s.Phone, 30), ("Het WhatsApp-nummer", s.WhatsAppPhone, 30),
            ("Het btw-nummer", s.VatNumber, 30), ("De link naar je Facebook", s.FacebookUrl, 500),
            ("De link naar je Instagram", s.InstagramUrl, 500), ("De link naar je TikTok", s.TikTokUrl, 500),
            ("De link naar je Google-bedrijfsprofiel", s.GoogleBusinessUrl, 500),
        }
        .Where(f => f.Value?.Trim().Length > f.Max)
        .Select(f => $"{f.Field} mag hoogstens {f.Max} tekens lang zijn.");

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
