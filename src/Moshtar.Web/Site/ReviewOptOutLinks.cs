using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Moshtar.Application.Reviews;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Reviews;

namespace Moshtar.Web.Site;

/// <summary>
/// De afmeldlink onderaan een reviewvraag. Het token is de verhuurder en het e-mailadres, versleuteld en ondertekend
/// met ASP.NET Data Protection: wie het wijzigt, of het gebruikt op het domein van een andere verhuurder, meldt niemand
/// af. Het token verloopt niet, zodat ook een oude reviewvraag nog werkt; het e-mailadres staat niet leesbaar in de link.
/// </summary>
public sealed class ReviewOptOutLinks(IDataProtectionProvider dataProtection, ITenantContext tenantContext) : IReviewOptOutLinks
{
    /// <summary>De querystring-sleutel met het token.</summary>
    public const string TokenKey = "code";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Moshtar.Reviews.OptOut.v1");

    public string For(string host, string culture, string email)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Geen verhuurder gekend.");
        var token = _protector.Protect($"{tenant.Id:N}\n{ReviewOptOut.Normalize(email)}");
        return Url(host, culture, token);
    }

    public string Example(string host, string culture) => Url(host, culture, "voorbeeld");

    /// <summary>Het e-mailadres in dit token, als het ongewijzigd is en bij de huidige verhuurder hoort; anders null.</summary>
    public string? EmailFrom(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || tenantContext.Tenant is not { } tenant) return null;
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
        return payload.Split('\n') is [var tenantId, var email] && tenantId == tenant.Id.ToString("N") && email.Length > 0 ? email : null;
    }

    private string Url(string host, string culture, string token)
    {
        var defaultCulture = tenantContext.Tenant?.DefaultCulture ?? "nl";
        return $"https://{host}{SiteLinks.Href(SiteRoute.ReviewOptOut, culture, defaultCulture)}?{TokenKey}={Uri.EscapeDataString(token)}";
    }
}
