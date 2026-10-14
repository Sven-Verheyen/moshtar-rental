using SkiaSharp;

namespace Moshtar.Web.Tests;

/// <summary>Afbeeldingen om te uploaden in de tests.</summary>
internal static class TestImages
{
    /// <summary>Een effen PNG van deze grootte.</summary>
    public static MemoryStream Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png, new SKColor(233, 30, 99));

    /// <summary>Een effen JPEG van deze grootte.</summary>
    public static MemoryStream Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg, new SKColor(30, 99, 233));

    /// <summary>De breedte van een gedownloade afbeelding.</summary>
    public static int WidthOf(byte[] bytes)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(bytes)) ?? throw new InvalidOperationException("Geen afbeelding.");
        return codec.Info.Width;
    }

    private static MemoryStream Encode(int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return new MemoryStream(data.ToArray());
    }
}
