using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Tenancy;

/// <summary>De bedrijfsgegevens en het werkgebied van de verhuurder, zoals bewaard.</summary>
public sealed record BusinessDetailsSettings
{
    public string? Street { get; init; }
    public string? PostalCode { get; init; }
    public string? City { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? WhatsAppPhone { get; init; }
    public string? VatNumber { get; init; }
    public string? FacebookUrl { get; init; }
    public string? InstagramUrl { get; init; }
    public string? TikTokUrl { get; init; }
    public string? GoogleBusinessUrl { get; init; }
    /// <summary>Het GA4-meet-ID, bv. G-ABC123XYZ; leeg betekent geen Google Analytics.</summary>
    public string? GoogleAnalyticsId { get; init; }
    /// <summary>De gemeenten van het werkgebied.</summary>
    public IReadOnlyList<string> ServiceArea { get; init; } = [];
    /// <summary>Kunnen klanten zelf afhalen, of levert de verhuurder altijd?</summary>
    public bool OffersPickup { get; init; }
    /// <summary>De standaarden voor de rondes in de Planning: vertrekuren, opbouw- en afbouwtijd, breedte van het venster.</summary>
    public RoundSettings Rounds { get; init; } = RoundSettings.Default;
}

/// <summary>Een ingevuld veld is niet geldig; <see cref="Problems"/> zegt wat er mis is.</summary>
public sealed class BusinessDetailsException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>De bedrijfsgegevens van de huidige verhuurder beheren, in het back office.</summary>
public interface IBusinessDetailsAdministration
{
    Task<BusinessDetailsSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Bewaart alle velden; leeg wist een veld. Gooit een <see cref="BusinessDetailsException"/> bij een ongeldige waarde.</summary>
    Task SaveAsync(BusinessDetailsSettings settings, CancellationToken ct = default);
}
