using Moshtar.Application.Catalog;
using Moshtar.Application.Prices;

namespace Moshtar.Web.Components.Admin;

/// <summary>
/// De categorieën, artikelen en pakketten waarop een prijsregel kan werken, met hun namen in de standaardtaal: voor de
/// keuzelijsten en de kolom met het doel in de editors van de pagina Prijsregels.
/// </summary>
public sealed class PricingTargetNames(
    IReadOnlyList<CategoryDetails> categories, IReadOnlyList<RentalItemSummary> items, IReadOnlyList<BundleSummary> bundles, string culture)
{
    /// <summary>Zolang er nog niets geladen is.</summary>
    public static readonly PricingTargetNames None = new([], [], [], "nl");

    public IReadOnlyList<CategoryDetails> Categories { get; } = categories;
    public IReadOnlyList<RentalItemSummary> Items { get; } = items;
    public IReadOnlyList<BundleSummary> Bundles { get; } = bundles;

    public static async Task<PricingTargetNames> LoadAsync(
        ICategoryAdministration categories, IRentalItemAdministration items, IBundleAdministration bundles, string culture) =>
        new(await categories.GetAllAsync(), await items.GetAllAsync(), await bundles.GetAllAsync(), culture);

    /// <summary>Waarop de regel werkt, met de namen in de standaardtaal; wat intussen verwijderd is, valt weg.</summary>
    public string Text(PricingTarget target)
    {
        if (target.All) return "Alles";
        var categories = target.CategoryIds.Select(CategoryName).Where(n => n.Length > 0);
        var chosen = target.RentalItemIds.Select(ItemName).Concat(target.BundleIds.Select(BundleName)).Where(n => n.Length > 0);
        return string.Join(", ", categories.Select(c => $"Categorie {c}").Concat(chosen));
    }

    public string Name(CategoryDetails category) => category.Texts[culture].Name.Value;

    public string CategoryName(Guid id) => Categories.FirstOrDefault(c => c.Id == id) is { } category ? Name(category) : "";

    public string ItemName(Guid id) => Items.FirstOrDefault(i => i.Id == id)?.Name ?? "";

    public string BundleName(Guid id) => Bundles.FirstOrDefault(b => b.Id == id)?.Name ?? "";

    /// <summary>De naam van een taal in het back office, bv. "Nederlands".</summary>
    public static string LanguageName(string culture) => culture switch
    {
        "nl" => "Nederlands",
        "fr" => "Frans",
        "en" => "Engels",
        _ => culture,
    };
}
