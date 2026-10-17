using Moshtar.Domain.Rounds;

namespace Moshtar.Application.Tenancy;

/// <summary>De bedrijfsgegevens van de verhuurder, zoals bewaard: wat klanten op de reservatiewebsite zien.</summary>
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
}

/// <summary>De instellingen van de planning, zoals bewaard: werkgebied, zelf afhalen en de rondes.</summary>
public sealed record PlanningSettings
{
    /// <summary>De gemeenten van het werkgebied.</summary>
    public IReadOnlyList<string> ServiceArea { get; init; } = [];
    /// <summary>Kunnen klanten zelf afhalen, of levert de verhuurder altijd?</summary>
    public bool OffersPickup { get; init; }
    /// <summary>De standaarden voor de rondes in de Planning: vertrekuren, opbouw- en afbouwtijd, breedte van het venster.</summary>
    public RoundSettings Rounds { get; init; } = RoundSettings.Default;
}

/// <summary>De instellingen van de reviewvragen, zoals bewaard.</summary>
public sealed record ReviewRequestSettings
{
    /// <summary>De reviewlink: de link van Google waarmee een klant meteen een review schrijft ("Vraag om reviews").</summary>
    public string? ReviewLink { get; init; }
    /// <summary>De schakelaar "Reviewvragen sturen"; kan alleen aan met een reviewlink.</summary>
    public bool SendReviewRequests { get; init; }
    /// <summary>Sinds wanneer de reviewvragen aan staan, of null als ze uit staan. Alleen te lezen: bewaren zet het zelf.</summary>
    public DateTimeOffset? ReviewRequestsSince { get; init; }
}

/// <summary>Een ingevuld veld is niet geldig; <see cref="Problems"/> zegt wat er mis is.</summary>
public sealed class BusinessDetailsException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// De gegevens van de huidige verhuurder beheren, in het back office: de bedrijfsgegevens, de instellingen van de planning,
/// de reviewvragen en Google Analytics. Elk deel heeft een eigen pagina en wordt apart bewaard: bewaren van het ene deel
/// laat de velden van de andere ongemoeid. Bewaren gooit een <see cref="BusinessDetailsException"/> bij een ongeldige waarde;
/// leeg wist een veld.
/// </summary>
public interface IBusinessDetailsAdministration
{
    Task<BusinessDetailsSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Bewaart de bedrijfsgegevens (Instellingen › Bedrijfsgegevens).</summary>
    Task SaveAsync(BusinessDetailsSettings settings, CancellationToken ct = default);

    Task<PlanningSettings> GetPlanningAsync(CancellationToken ct = default);

    /// <summary>Bewaart werkgebied, zelf afhalen en rondes (Planning › Instellingen).</summary>
    Task SavePlanningAsync(PlanningSettings settings, CancellationToken ct = default);

    Task<ReviewRequestSettings> GetReviewRequestsAsync(CancellationToken ct = default);

    /// <summary>Bewaart de reviewlink en de schakelaar "Reviewvragen sturen" (Klantenbeheer › Reviewvragen).</summary>
    Task SaveReviewRequestsAsync(ReviewRequestSettings settings, CancellationToken ct = default);

    /// <summary>Het GA4-meet-ID, bv. G-ABC123XYZ, of null: geen Google Analytics.</summary>
    Task<string?> GetGoogleAnalyticsIdAsync(CancellationToken ct = default);

    /// <summary>Bewaart het GA4-meet-ID (Website en mails › Huisstijl).</summary>
    Task SaveGoogleAnalyticsIdAsync(string? googleAnalyticsId, CancellationToken ct = default);
}
