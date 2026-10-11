using Moshtar.Domain.Marketing;

namespace Moshtar.Application.Marketing;

/// <summary>De marketingcontacten van de huidige verhuurder, voor de beheerder.</summary>
public interface IMarketingContactAdministration
{
    /// <summary>Alle marketingcontacten, de meest recente toestemming eerst.</summary>
    Task<IReadOnlyList<MarketingContactView>> ListAsync(CancellationToken ct = default);
}

public record MarketingContactView(string Email, string Culture, DateTime ConsentedAtUtc, MarketingContactSource Source);
