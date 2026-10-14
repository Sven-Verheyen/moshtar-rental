using System.Text.RegularExpressions;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using SkiaSharp;

namespace Moshtar.Infrastructure.Images;

public class ImageOptions
{
    /// <summary>Connection string van Azure Blob Storage. Leeg: de lokale opslag in de app (ontwikkeling en tests).</summary>
    public string? AzureBlobStorageConnectionString { get; set; }

    /// <summary>De container in Azure Blob Storage, met publieke leestoegang tot de blobs.</summary>
    public string Container { get; set; } = "afbeeldingen";

    /// <summary>
    /// Het begin van elk publiek adres, bv. een CDN voor de container. Leeg: de container zelf (Azure), of
    /// https://&lt;hoofddomein van de verhuurder&gt;/afbeeldingen (lokale opslag).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>De map van de lokale opslag. Leeg: App_Data/afbeeldingen in de huidige map.</summary>
    public string? LocalPath { get; set; }
}

/// <summary>
/// Wat elke afbeeldingenopslag gemeen heeft: controleren, verkleinen, sleutels per verhuurder en de publieke adressen.
/// Een sleutel is "&lt;verhuurder&gt;/&lt;afbeelding&gt;"; elke breedte staat als "&lt;sleutel&gt;/&lt;breedte&gt;.webp".
/// </summary>
internal abstract partial class ImageStore(ITenantContext tenantContext) : IImageStore
{
    private static readonly HashSet<SKEncodedImageFormat> AcceptedFormats =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp, SKEncodedImageFormat.Gif];

    /// <summary>Meer pixels dan dit laadt Moshtar niet, zodat één upload het geheugen niet opgebruikt.</summary>
    private const long MaxPixels = 50_000_000;

    /// <summary>De kwaliteit van de WebP-versies (0-100).</summary>
    private const int Quality = 80;

    public async Task<string> UploadAsync(Stream content, CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var bytes = await ReadAtMostAsync(content, ImageUpload.MaxBytes, ct);
        var versions = Resize(bytes);

        var key = $"{tenantId:N}/{Guid.NewGuid():N}";
        foreach (var (width, data) in versions)
        {
            using var output = new MemoryStream(data);
            await SaveAsync(PathOf(key, width), output, ct);
        }
        return key;
    }

    /// <summary>
    /// De afbeelding als WebP in elke breedte uit <see cref="ImageUpload.Widths"/>, rechtop gezet zoals de camera hem nam.
    /// Een WebP-versie bevat geen metadata meer, zoals de GPS-locatie. Van een geanimeerde GIF alleen het eerste beeld.
    /// </summary>
    private static List<(int Width, byte[] Data)> Resize(byte[] bytes)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        if (codec is null || !AcceptedFormats.Contains(codec.EncodedFormat))
            throw new ImageRejectedException(ImageUpload.WrongFormat);
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new ImageRejectedException(ImageUpload.TooLarge);

        using var decoded = SKBitmap.Decode(codec, codec.Info.WithColorType(SKImageInfo.PlatformColorType).WithAlphaType(SKAlphaType.Premul))
            ?? throw new ImageRejectedException(ImageUpload.WrongFormat);
        using var upright = Upright(decoded, codec.EncodedOrigin);

        var versions = new List<(int, byte[])>();
        foreach (var width in ImageUpload.Widths)
        {
            var target = Math.Min(width, upright.Width);
            var height = Math.Max(1, (int)Math.Round((double)upright.Height * target / upright.Width));
            using var resized = target == upright.Width
                ? upright.Copy()
                : upright.Resize(new SKImageInfo(target, height, upright.ColorType, upright.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Webp, Quality);
            versions.Add((width, data.ToArray()));
        }
        return versions;
    }

    /// <summary>Een kopie die rechtop staat, volgens de oriëntatie die de camera meegaf.</summary>
    private static SKBitmap Upright(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        // x' = ScaleX·x + SkewX·y + TransX, y' = SkewY·x + ScaleY·y + TransY
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (matrix is null) return source.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var upright = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(upright);
        canvas.SetMatrix(matrix.Value);
        canvas.DrawBitmap(source, 0, 0);
        return upright;
    }

    public string PublicUrl(string key, int width)
    {
        if (!ValidKey().IsMatch(key)) throw new ArgumentException($"Geen geldige sleutel van een afbeelding: '{key}'.", nameof(key));
        var stored = ImageUpload.Widths.FirstOrDefault(w => w >= width, ImageUpload.Widths[^1]);
        return $"{PublicBaseUrl.TrimEnd('/')}/{PathOf(key, stored)}";
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!ValidKey().IsMatch(key) || !key.StartsWith($"{TenantId:N}/", StringComparison.Ordinal))
            throw new ImageRejectedException("Deze afbeelding hoort niet bij jouw verhuurder.");
        foreach (var width in ImageUpload.Widths)
            await DeleteFileAsync(PathOf(key, width), ct);
    }

    /// <summary>Het begin van elk publiek adres, zonder het pad van de afbeelding.</summary>
    protected abstract string PublicBaseUrl { get; }

    /// <summary>Bewaart een bestand op dit pad (een nieuw pad, nooit een bestaand).</summary>
    protected abstract Task SaveAsync(string path, Stream content, CancellationToken ct);

    /// <summary>Verwijdert het bestand op dit pad, als het bestaat.</summary>
    protected abstract Task DeleteFileAsync(string path, CancellationToken ct);

    protected ITenantContext TenantContext => tenantContext;

    private Guid TenantId => tenantContext.Tenant?.Id ?? throw new InvalidOperationException("Geen verhuurder gekend.");

    private static string PathOf(string key, int width) => $"{key}/{width}.webp";

    private static async Task<byte[]> ReadAtMostAsync(Stream content, long max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max) throw new ImageRejectedException(ImageUpload.TooLarge);
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw new ImageRejectedException(ImageUpload.WrongFormat);
        return buffer.ToArray();
    }

    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}$")]
    private static partial Regex ValidKey();
}
