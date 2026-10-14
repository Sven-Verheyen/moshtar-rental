using Moshtar.Application.Images;
using Moshtar.Domain.Tenants;

namespace Moshtar.Application.Tenancy;

/// <summary>De publieke adressen van logo en sfeerfoto en de kleur van de verhuurder, zoals bewaard.</summary>
public sealed record BrandingSettings(string? LogoUrl, string? PrimaryColor, string? HeroImageUrl = null);

/// <summary>De afbeeldingen van de huisstijl.</summary>
public enum BrandingImage
{
    Logo,
    HeroImage,
}

/// <summary>De kleur is niet geldig; <see cref="Problems"/> zegt wat er mis is.</summary>
public sealed class BrandingException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>De huisstijl van de huidige verhuurder beheren, in het back office (alleen een Beheerder).</summary>
public interface IBrandingAdministration
{
    Task<BrandingSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Bewaart de kleur; leeg wist ze. Gooit een <see cref="BrandingException"/> bij een ongeldige kleur.</summary>
    Task SaveColorAsync(string? color, CancellationToken ct = default);

    /// <summary>
    /// Uploadt het logo of de sfeerfoto; een vorige afbeelding wordt uit de opslag opgeruimd. Gooit een
    /// <see cref="ImageRejectedException"/> als het geen gangbare afbeelding is of ze te groot is.
    /// </summary>
    Task UploadAsync(BrandingImage image, Stream content, CancellationToken ct = default);

    /// <summary>Verwijdert het logo of de sfeerfoto, ook uit de opslag.</summary>
    Task RemoveAsync(BrandingImage image, CancellationToken ct = default);

    /// <summary>De kop van een klantmail met dit logo en deze kleur, als HTML; leeg als er geen kop is.</summary>
    string PreviewHeader(BrandingSettings settings);
}

/// <summary>De publieke adressen van logo en sfeerfoto van een verhuurder, in de breedte waarin ze getoond worden.</summary>
public static class BrandingImages
{
    /// <summary>Het logo staat klein in de kop van de website en de mails.</summary>
    public const int LogoWidth = 400;

    /// <summary>De sfeerfoto staat over de volle breedte van de home.</summary>
    public const int HeroImageWidth = 1600;

    public static string? LogoUrl(this IImageStore images, Tenant? tenant) =>
        tenant?.LogoImageKey is { } key ? images.PublicUrl(key, LogoWidth) : null;

    public static string? HeroImageUrl(this IImageStore images, Tenant? tenant) =>
        tenant?.HeroImageKey is { } key ? images.PublicUrl(key, HeroImageWidth) : null;
}
