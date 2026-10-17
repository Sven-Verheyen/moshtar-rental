using System.Security.Claims;
using Moshtar.Web.Identity;
using MudBlazor;

namespace Moshtar.Web.Admin;

/// <summary>Een pagina in het menu van het back office. <paramref name="Role"/> leeg: iedereen die mag inloggen.</summary>
public sealed record BackOfficePage(string Title, string Href, string Icon, string? Role = null)
{
    public bool IsVisibleTo(ClaimsPrincipal user) => Role is null || user.IsInRole(Role);
}

/// <summary>Een onderdeel van het back office: een openklapbare groep pagina's in het menu, zonder eigen overzichtspagina.</summary>
public sealed record BackOfficePart(string Title, string Icon, IReadOnlyList<BackOfficePage> Pages);

/// <summary>
/// Waar een adres in het back office hoort: het onderdeel, de menupagina en of het een subpagina ervan is
/// (bv. <c>admin/klanten/{id}</c> onder Klantenbeheer › Klanten).
/// </summary>
public sealed record BackOfficeLocation(BackOfficePart Part, BackOfficePage Page, bool IsSubpage);

/// <summary>
/// De indeling van het back office in onderdelen en pagina's. Menu, open onderdeel en kruimelpad komen allemaal hieruit.
/// Een pagina hoort bij precies één onderdeel; de volgorde hier is de volgorde in het menu. Een subpagina hoeft hier niet
/// in te staan: ze hoort bij de menupagina met het langste adres waarmee haar adres begint.
/// </summary>
public static class BackOfficeMenu
{
    public const string ShortagesHref = "admin/tekorten";

    public static IReadOnlyList<BackOfficePart> Parts { get; } =
    [
        new("Planning", Icons.Material.Filled.CalendarToday,
        [
            new("Planning", "admin/planning", Icons.Material.Filled.CalendarToday),
            // Zonder dag: de leverlijst toont dan die van vandaag.
            new("Leverlijst", "admin/planning/leverlijst", Icons.Material.Filled.LocalShipping),
        ]),
        new("Reservaties", Icons.Material.Filled.EventNote,
        [
            new("Reservaties", "admin/reservaties", Icons.Material.Filled.EventNote),
            new("Nieuwe reservatie", "admin/reservaties/nieuw", Icons.Material.Filled.AddCircleOutline),
            new("Tekorten", ShortagesHref, Icons.Material.Filled.ReportProblem),
            new("Open winkelkarren", "admin/winkelkarren", Icons.Material.Filled.ShoppingCart),
        ]),
        new("Klantenbeheer", Icons.Material.Filled.Contacts,
        [
            new("Klanten", "admin/klanten", Icons.Material.Filled.Contacts),
            new("Marketingcontacten", "admin/marketingcontacten", Icons.Material.Filled.Campaign, Roles.Administrator),
        ]),
        new("Artikelbeheer", Icons.Material.Filled.Toys,
        [
            new("Artikelen", "admin/artikelen", Icons.Material.Filled.Toys, Roles.Administrator),
            new("Pakketten", "admin/pakketten", Icons.Material.Filled.Inventory2, Roles.Administrator),
            new("Categorieën", "admin/categorieen", Icons.Material.Filled.Category, Roles.Administrator),
            new("Uitgelicht", "admin/uitgelicht", Icons.Material.Filled.Star, Roles.Administrator),
            new("Prijsregels", "admin/prijsregels", Icons.Material.Filled.Sell, Roles.Administrator),
            new("Blokkeringen", "admin/blokkeringen", Icons.Material.Filled.Block, Roles.Administrator),
        ]),
        new("Website en mails", Icons.Material.Filled.Language,
        [
            new("Websiteteksten", "admin/websiteteksten", Icons.Material.Filled.TextFields, Roles.Administrator),
            new("Huisstijl", "admin/huisstijl", Icons.Material.Filled.Palette, Roles.Administrator),
            new("Mailsjablonen", "admin/mailsjablonen", Icons.Material.Filled.Email, Roles.Administrator),
        ]),
        new("Instellingen", Icons.Material.Filled.Settings,
        [
            new("Bedrijfsgegevens", "admin/bedrijfsgegevens", Icons.Material.Filled.Business, Roles.Administrator),
            new("Gebruikers", "admin/gebruikers", Icons.Material.Filled.People, Roles.Administrator),
        ]),
    ];

    /// <summary>De onderdelen met enkel de pagina's die de gebruiker mag openen; een onderdeel zonder zulke pagina's valt weg.</summary>
    public static IReadOnlyList<BackOfficePart> VisibleTo(ClaimsPrincipal user) =>
        Parts.Select(part => part with { Pages = part.Pages.Where(page => page.IsVisibleTo(user)).ToList() })
            .Where(part => part.Pages.Count > 0)
            .ToList();

    /// <summary>
    /// Het onderdeel en de menupagina waar een adres (relatief tegenover de basis, zoals <c>admin/klanten/importeren?x=1</c>) bij hoort,
    /// of null buiten het back office. <c>admin</c> zelf is de startpagina Planning.
    /// </summary>
    public static BackOfficeLocation? Locate(string relativeUrl)
    {
        var path = relativeUrl.Split('?', '#')[0].Trim('/').ToLowerInvariant();
        if (path == "admin") path = Parts[0].Pages[0].Href;
        return Parts
            .SelectMany(part => part.Pages, (part, page) => (part, page))
            .Where(x => path == x.page.Href || path.StartsWith(x.page.Href + "/", StringComparison.Ordinal))
            .OrderByDescending(x => x.page.Href.Length)
            .Select(x => new BackOfficeLocation(x.part, x.page, path != x.page.Href))
            .FirstOrDefault();
    }
}
