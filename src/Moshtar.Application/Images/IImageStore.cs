namespace Moshtar.Application.Images;

/// <summary>Wat een upload moet zijn om aanvaard te worden, en in welke breedtes Moshtar hem bewaart.</summary>
public static class ImageUpload
{
    /// <summary>De grootste upload die aanvaard wordt: 10 MB.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>De breedtes in pixels waarin elke afbeelding bewaard wordt, van klein naar groot. Kleinere worden niet vergroot.</summary>
    public static readonly IReadOnlyList<int> Widths = [400, 800, 1600];

    /// <summary>De aanvaarde formaten, voor het accept-attribuut van een bestandskiezer.</summary>
    public const string Accept = "image/jpeg,image/png,image/webp,image/gif";

    public const string WrongFormat = "Dit bestand is geen afbeelding in een gangbaar formaat (JPG, PNG, WebP of GIF).";
    public const string TooLarge = "Deze afbeelding is te groot: maximaal 10 MB.";
}

/// <summary>Een upload of verwijdering die niet kan; <see cref="Exception.Message"/> zegt waarom, voor de gebruiker.</summary>
public sealed class ImageRejectedException(string message) : Exception(message);

/// <summary>
/// De afbeeldingenopslag van Moshtar (ADR 0005), per verhuurder gescheiden: foto's, logo en sfeerfoto. Een upload krijgt
/// een sleutel; die sleutel bewaart wie de afbeelding gebruikt, het publieke adres wordt eruit afgeleid.
/// In productie Azure Blob Storage, zonder connection string (lokaal en in de tests) een lokale opslag in de app.
/// </summary>
public interface IImageStore
{
    /// <summary>
    /// Bewaart een afbeelding van de huidige verhuurder, verkleind naar elke breedte uit <see cref="ImageUpload.Widths"/>
    /// in een web-vriendelijk formaat, en geeft de sleutel terug. Een upload overschrijft nooit een andere afbeelding.
    /// Gooit een <see cref="ImageRejectedException"/> als het geen gangbaar formaat is of groter dan <see cref="ImageUpload.MaxBytes"/>.
    /// </summary>
    Task<string> UploadAsync(Stream content, CancellationToken ct = default);

    /// <summary>
    /// Het volledige publieke adres van de afbeelding met deze sleutel, in de kleinste bewaarde breedte die minstens
    /// <paramref name="width"/> pixels breed is (of de grootste).
    /// </summary>
    string PublicUrl(string key, int width);

    /// <summary>
    /// Verwijdert de afbeelding met deze sleutel, in alle breedtes. Gooit een <see cref="ImageRejectedException"/> als
    /// ze niet van de huidige verhuurder is.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public static class ImageStoreExtensions
{
    /// <summary>Alle bewaarde breedtes voor het srcset-attribuut van een &lt;img&gt;, bv. "https://…/400.webp 400w, …".</summary>
    public static string SrcSet(this IImageStore images, string key) =>
        string.Join(", ", ImageUpload.Widths.Select(w => $"{images.PublicUrl(key, w)} {w}w"));
}
