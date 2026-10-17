using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Tenancy;

/// <summary>
/// Alle delen staan op de verhuurder zelf; elk deel schrijft alleen zijn eigen kolommen, zodat bewaren op de ene pagina
/// niets wist of terugzet van een andere.
/// </summary>
internal sealed class BusinessDetailsAdministration(AppDbContext db, ITenantContext tenantContext, ITenantStore store, TimeProvider clock) : IBusinessDetailsAdministration
{
    public async Task<BusinessDetailsSettings> GetAsync(CancellationToken ct = default)
    {
        var t = await ReadAsync(ct);
        return new BusinessDetailsSettings
        {
            Street = t.Street, PostalCode = t.PostalCode, City = t.City,
            Email = t.ContactEmail, Phone = t.ContactPhone, WhatsAppPhone = t.WhatsAppPhone, VatNumber = t.VatNumber,
            FacebookUrl = t.FacebookUrl, InstagramUrl = t.InstagramUrl, TikTokUrl = t.TikTokUrl, GoogleBusinessUrl = t.GoogleBusinessUrl,
        };
    }

    public Task SaveAsync(BusinessDetailsSettings s, CancellationToken ct = default) =>
        SavePartAsync(
            BusinessDetails.Problems(s.Email, s.Phone, s.WhatsAppPhone, s.FacebookUrl, s.InstagramUrl, s.TikTokUrl, s.GoogleBusinessUrl)
                .Concat(TooLong(
                    ("De straat", s.Street, 200), ("De postcode", s.PostalCode, 20), ("De gemeente", s.City, 100),
                    ("Het e-mailadres", s.Email, 254), ("Het telefoonnummer", s.Phone, 30), ("Het WhatsApp-nummer", s.WhatsAppPhone, 30),
                    ("Het btw-nummer", s.VatNumber, 30), ("De link naar je Facebook", s.FacebookUrl, 500),
                    ("De link naar je Instagram", s.InstagramUrl, 500), ("De link naar je TikTok", s.TikTokUrl, 500),
                    ("De link naar je Google-bedrijfsprofiel", s.GoogleBusinessUrl, 500))),
            tenant =>
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
            },
            ct);

    public async Task<PlanningSettings> GetPlanningAsync(CancellationToken ct = default)
    {
        var t = await ReadAsync(ct);
        return new PlanningSettings { ServiceArea = t.ServiceArea, OffersPickup = t.OffersPickup, Rounds = t.RoundSettings };
    }

    public Task SavePlanningAsync(PlanningSettings s, CancellationToken ct = default)
    {
        var serviceArea = BusinessDetails.ServiceArea(string.Join('\n', s.ServiceArea)).ToList();
        return SavePartAsync(s.Rounds.Problems(), tenant =>
        {
            tenant.ServiceArea = [.. serviceArea];
            tenant.OffersPickup = s.OffersPickup;
            tenant.RoundSettings = s.Rounds;
        }, ct);
    }

    public async Task<ReviewRequestSettings> GetReviewRequestsAsync(CancellationToken ct = default)
    {
        var t = await ReadAsync(ct);
        return new ReviewRequestSettings
        {
            ReviewLink = t.ReviewLink, SendReviewRequests = t.ReviewRequestsSince is not null, ReviewRequestsSince = t.ReviewRequestsSince,
        };
    }

    public async Task SaveReviewRequestsAsync(ReviewRequestSettings s, CancellationToken ct = default)
    {
        // Het tijdstip van aanzetten blijft staan zolang de schakelaar aan blijft; opnieuw aanzetten begint opnieuw. Het
        // wordt bepaald op de bewaarde verhuurder (die eerst aan de beurt is); de kopie in dit scherm neemt het over.
        DateTimeOffset? since = null;
        await SavePartAsync(
            (BusinessDetails.ReviewLinkProblem(s.ReviewLink, s.SendReviewRequests) is { } review ? [review] : Array.Empty<string>())
                .Concat(TooLong(("De Google-reviewlink", s.ReviewLink, 500))),
            tenant =>
            {
                tenant.ReviewLink = BusinessDetails.Link(s.ReviewLink);
                since ??= s.SendReviewRequests ? tenant.ReviewRequestsSince ?? clock.GetUtcNow() : null;
                tenant.ReviewRequestsSince = since;
            },
            ct);
    }

    public async Task<string?> GetGoogleAnalyticsIdAsync(CancellationToken ct = default) => (await ReadAsync(ct)).GoogleAnalyticsId;

    public Task SaveGoogleAnalyticsIdAsync(string? googleAnalyticsId, CancellationToken ct = default) =>
        SavePartAsync(
            BusinessDetails.GoogleAnalyticsProblem(googleAnalyticsId) is { } problem ? [problem] : [],
            tenant => tenant.GoogleAnalyticsId = BusinessDetails.GoogleAnalyticsId(googleAnalyticsId),
            ct);

    private async Task<Tenant> ReadAsync(CancellationToken ct)
    {
        var id = Tenant.Id;
        return await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == id, ct);
    }

    /// <summary>Bewaart één deel als er niets mis is, en toont het meteen overal.</summary>
    private async Task SavePartAsync(IEnumerable<string> problems, Action<Tenant> apply, CancellationToken ct)
    {
        var list = problems.ToList();
        if (list.Count > 0)
            throw new BusinessDetailsException(list);

        var id = Tenant.Id;
        var tenant = await db.Tenants.SingleAsync(t => t.Id == id, ct);
        apply(tenant);
        await db.SaveChangesAsync(ct);
        // Ook wat in dit scherm (dezelfde Blazor-circuit) en in de cache van verhuurders zit, toont meteen de nieuwe gegevens.
        apply(Tenant);
        store.Forget(id);
    }

    /// <summary>Velden die langer zijn dan de database toelaat (zie TenantConfiguration).</summary>
    private static IEnumerable<string> TooLong(params (string Field, string? Value, int Max)[] fields) =>
        fields
            .Where(f => f.Value?.Trim().Length > f.Max)
            .Select(f => $"{f.Field} mag hoogstens {f.Max} tekens lang zijn.");

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Tenant Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
}
