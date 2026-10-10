namespace Moshtar.Application.Tenancy;

/// <summary>Het logo-adres, de kleur en het sfeerfoto-adres van de verhuurder, zoals bewaard.</summary>
public sealed record BrandingSettings(string? LogoUrl, string? PrimaryColor, string? HeroImageUrl = null);

/// <summary>Het logo-adres, de kleur of het sfeerfoto-adres is niet geldig; <see cref="Problems"/> zegt wat er mis is.</summary>
public sealed class BrandingException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>De huisstijl van de huidige verhuurder beheren, in het back office.</summary>
public interface IBrandingAdministration
{
    Task<BrandingSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Bewaart logo, kleur en sfeerfoto; leeg wist ze. Gooit een <see cref="BrandingException"/> bij een ongeldige waarde.</summary>
    Task SaveAsync(BrandingSettings settings, CancellationToken ct = default);

    /// <summary>De kop van een klantmail met dit logo en deze kleur, als HTML; leeg als er geen kop is.</summary>
    string PreviewHeader(BrandingSettings settings);
}
