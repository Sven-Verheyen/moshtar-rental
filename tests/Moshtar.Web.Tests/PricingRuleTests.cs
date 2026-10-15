using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Prices;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder beheert de prijsregels van zijn verhuur op de pagina Prijsregels (alleen Beheerder). Het eerste
/// tabblad: combinatiekortingen, met naam per taal, een categorie of gekozen artikelen en pakketten, een minimum aantal
/// stuks, een kortingspercentage, aan/uit en een optionele einddatum.
/// </summary>
public sealed class PricingRuleTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task A_combo_discount_is_created_changed_switched_off_and_deleted()
    {
        await using var scope = await NewTenantAsync("prijsregel-beheer");
        var castles = await Categories(scope).CreateAsync([new("nl", "Springkastelen")], Guid.Empty);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var id = await Rules(scope).CreateComboAsync(new ComboDiscountInput(
            [new("nl", " 2e springkasteel 40% korting "), new("fr", "2e château -40%"), new("en", "")],
            PricingTarget.Categories(castles), MinimumQuantity: 2, Percentage: 40));

        var combo = Assert.Single(await Rules(scope).GetCombosAsync());
        Assert.Equal(id, combo.Id);
        Assert.Equal([("nl", "2e springkasteel 40% korting"), ("fr", "2e château -40%")], combo.Input.Names.Select(n => (n.Culture, n.Name)));
        Assert.Equal([castles], combo.Input.Target.CategoryIds);
        Assert.Equal((2, 40m, true, (DateOnly?)null, false), (combo.Input.MinimumQuantity, combo.Input.Percentage, combo.Input.IsActive, combo.Input.EndDate, combo.IsExpired));

        var pirate = await Items(scope).CreateAsync(new([new RentalItemText("nl", "Piraat")], castles, 1, 100, null, NoSpecs));
        await Rules(scope).SaveComboAsync(id, new ComboDiscountInput(
            [new("nl", "Piraat in combinatie")], PricingTarget.Chosen([pirate], []), 3, 25, IsActive: true, EndDate: today.AddDays(10)));
        combo = Assert.Single(await Rules(scope).GetCombosAsync());
        Assert.Equal(("Piraat in combinatie", 3, 25m, today.AddDays(10)), (combo.Input.Names.Single().Name, combo.Input.MinimumQuantity, combo.Input.Percentage, combo.Input.EndDate));
        Assert.Empty(combo.Input.Target.CategoryIds);
        Assert.Equal([pirate], combo.Input.Target.RentalItemIds);

        await Rules(scope).SetActiveAsync(id, false);
        Assert.False(Assert.Single(await Rules(scope).GetCombosAsync()).Input.IsActive);

        await Rules(scope).DeleteAsync(id);
        Assert.Empty(await Rules(scope).GetCombosAsync());
    }

    [Fact]
    public async Task An_invalid_combo_discount_is_refused()
    {
        await using var scope = await NewTenantAsync("prijsregel-ongeldig");

        var refused = await Assert.ThrowsAsync<PricingRuleException>(() => Rules(scope).CreateComboAsync(new ComboDiscountInput(
            [new("nl", " "), new("fr", "Remise")], PricingTarget.Chosen([], []), MinimumQuantity: 1, Percentage: 0)));
        Assert.Equal([
            "De naam in het Nederlands is leeg.",
            "Kies een categorie of minstens één artikel of pakket.",
            "Het minimum aantal stuks is minstens 2.",
            "Het kortingspercentage ligt tussen 1 en 100%.",
        ], refused.Problems);

        refused = await Assert.ThrowsAsync<PricingRuleException>(() => Rules(scope).CreateComboAsync(new ComboDiscountInput(
            [new("nl", "Te veel")], PricingTarget.Categories(Guid.NewGuid()), 2, 100.5m)));
        Assert.Equal(["Het kortingspercentage ligt tussen 1 en 100%."], refused.Problems);
        Assert.Empty(await Rules(scope).GetCombosAsync());
    }

    [Fact]
    public async Task Expired_rules_are_listed_at_the_bottom()
    {
        await using var scope = await NewTenantAsync("prijsregel-verlopen");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var target = PricingTarget.Categories(Guid.NewGuid());

        var expired = await Rules(scope).CreateComboAsync(new([new("nl", "A verlopen")], target, 2, 10, EndDate: today.AddDays(-1)));
        var current = await Rules(scope).CreateComboAsync(new([new("nl", "B loopt")], target, 2, 10, EndDate: today));
        var off = await Rules(scope).CreateComboAsync(new([new("nl", "C uit")], target, 2, 10, IsActive: false));

        var combos = await Rules(scope).GetCombosAsync();
        Assert.Equal([current, off, expired], combos.Select(c => c.Id));
        Assert.Equal([false, false, true], combos.Select(c => c.IsExpired));
    }

    [Fact]
    public async Task Pricing_rules_belong_to_one_rental_company()
    {
        await using (var scope = await NewTenantAsync("prijsregel-eigen"))
            await Rules(scope).CreateComboAsync(new([new("nl", "Van mij")], PricingTarget.Categories(Guid.NewGuid()), 2, 10));

        await using var other = await NewTenantAsync("prijsregel-ander");
        Assert.Empty(await Rules(other).GetCombosAsync());
    }

    [Fact]
    public async Task Administrator_sees_the_pricing_rules_page_with_its_tabs()
    {
        var client = await app.LoggedInClientAsync("prijsregels-beheerder@hopsakee.test", UserRole.Administrator);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Rules(scope).CreateComboAsync(new([new("nl", "2e springkasteel 40% korting")], PricingTarget.Categories(Guid.NewGuid()), 2, 40));

        var response = await client.GetAsync("/admin/prijsregels");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("href=\"admin/prijsregels\"", html);
        Assert.Contains("Combinatiekortingen", html);
        Assert.Contains("Periodeprijzen", html);
        Assert.Contains("Acties", html);
        Assert.Contains("2e springkasteel 40% korting", html);
    }

    [Fact]
    public async Task Staff_has_no_access_and_no_menu_item()
    {
        var client = await app.LoggedInClientAsync("prijsregels-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/prijsregels");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/prijsregels\"", await client.GetStringAsync("/admin/reservaties"));
    }

    private async Task<AsyncServiceScope> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        return await app.TenantScopeAsync(slug);
    }

    private static IPricingRuleAdministration Rules(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IPricingRuleAdministration>();

    private static ICategoryAdministration Categories(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ICategoryAdministration>();

    private static IRentalItemAdministration Items(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();
}
