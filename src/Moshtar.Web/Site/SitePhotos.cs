using Moshtar.Application.Images;

namespace Moshtar.Web.Site;

/// <summary>
/// De foto's van artikelen en pakketten op de reservatiewebsite. Een &lt;img&gt; krijgt alle bewaarde breedtes
/// (srcset) en zegt hoe breed hij getoond wordt (sizes), zodat de browser de kleinste passende versie laadt.
/// </summary>
public static class SitePhotos
{
    /// <summary>De breedte in src, voor een browser zonder srcset.</summary>
    public const int FallbackWidth = 800;

    /// <summary>Open Graph en JSON-LD: groot genoeg voor sociale media en zoekmachines.</summary>
    public const int ShareWidth = 1600;

    /// <summary>Een kaart in het aanbod: de hele breedte op een gsm, anders een kolom van ongeveer 23rem (site.css).</summary>
    public const string CardSizes = "(max-width: 40rem) 100vw, 23rem";

    /// <summary>Een tegel van een categorie op de home.</summary>
    public const string TileSizes = "(max-width: 30rem) 100vw, 17rem";

    /// <summary>De hoofdfoto op de artikelpagina: de hele breedte op een gsm, anders de helft van de pagina.</summary>
    public const string MainSizes = "(max-width: 44rem) 100vw, 36rem";

    /// <summary>De andere foto's op de artikelpagina, drie naast elkaar onder de hoofdfoto.</summary>
    public const string ThumbnailSizes = "(max-width: 44rem) 33vw, 12rem";

    /// <summary>Het volledige adres van de foto voor Open Graph en JSON-LD.</summary>
    public static string ShareUrl(this IImageStore images, string key) => images.PublicUrl(key, ShareWidth);
}
