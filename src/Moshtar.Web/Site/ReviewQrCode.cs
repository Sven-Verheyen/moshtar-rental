using Moshtar.Application.Tenancy;
using Moshtar.Domain.Tenants;
using QRCoder;

namespace Moshtar.Web.Site;

/// <summary>
/// De QR-code van de reviewlink van de verhuurder, als PNG op een publiek adres van zijn reservatiewebsite: de
/// reviewvraag toont ze als gewone afbeelding en de beheerder downloadt ze om af te drukken. Ze wordt telkens uit de
/// bewaarde link gemaakt, dus een andere link geeft meteen een andere QR-code. Zonder link: 404.
/// </summary>
public static class ReviewQrCode
{
    public const string Path = Application.Reviews.ReviewQrCodePath.Path;

    /// <summary>Pixels per blokje: groot genoeg om scherp af te drukken op een sticker of flyer.</summary>
    private const int PixelsPerModule = 12;

    public static IEndpointRouteBuilder MapReviewQrCode(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (HttpContext context, ITenantContext tenantContext) =>
        {
            if (context.Request.PathBase.HasValue) return Results.NotFound();
            if (BusinessDetails.Link(tenantContext.Tenant?.ReviewLink) is not { } link) return Results.NotFound();
            // Altijd opnieuw nagaan: na een gewijzigde link mag niemand nog de oude QR-code krijgen.
            context.Response.Headers.CacheControl = "no-cache";
            return Results.File(Png(link), "image/png", "review-qr.png");
        });
        return app;
    }

    /// <summary>De QR-code van deze link als PNG.</summary>
    public static byte[] Png(string link)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }
}
