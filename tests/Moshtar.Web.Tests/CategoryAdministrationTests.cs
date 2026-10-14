using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Website;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder beheert de categorieën in het back office: aanmaken, naam, slug en inleiding per taal, volgorde en
/// verwijderen. Een oude slug stuurt met een 301 door naar de huidige.
/// </summary>
public sealed class CategoryAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task A_category_is_created_with_a_name_in_the_default_language_and_its_slug_comes_from_the_name()
    {
        var (scope, user) = await NewTenantAsync("categorie-aanmaken");
        await using (scope)
        {
            var categories = Categories(scope);

            var id = await categories.CreateAsync([new("nl", " Luchtkastelen ")], user);

            var created = (await categories.GetAllAsync()).Last();
            Assert.Equal(id, created.Id);
            Assert.Equal(("Luchtkastelen", "luchtkastelen"), (created.Texts["nl"].Name.Value, created.Texts["nl"].Slug.Value));
            // Zonder Franse naam toont de site de naam in de standaardtaal.
            Assert.Equal(("Luchtkastelen", "luchtkastelen"), (created.Texts["fr"].Name.Value, created.Texts["fr"].Slug.Value));
            Assert.Equal(0, created.ItemCount);
        }
    }

    [Fact]
    public async Task A_new_category_gets_optional_names_and_adjusted_slugs_in_other_languages()
    {
        var (scope, user) = await NewTenantAsync("categorie-talen");
        await using (scope)
        {
            var categories = Categories(scope);

            await categories.CreateAsync([new("nl", "Luchtkastelen", "kastelen-in-de-lucht"), new("fr", "Châteaux d'air"), new("en", "")], user);

            var created = (await categories.GetAllAsync()).Last();
            Assert.Equal("kastelen-in-de-lucht", created.Texts["nl"].Slug.Value);
            Assert.Equal(("Châteaux d'air", "chateaux-d-air"), (created.Texts["fr"].Name.Value, created.Texts["fr"].Slug.Value));
            Assert.Equal("Luchtkastelen", created.Texts["en"].Name.Value);
        }
    }

    [Fact]
    public async Task A_category_without_a_name_in_the_default_language_is_refused()
    {
        var (scope, user) = await NewTenantAsync("categorie-zonder-naam");
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).CreateAsync([new("nl", " "), new("fr", "Jeux d'eau")], user));

            Assert.Equal(["De naam in het Nederlands is leeg."], refused.Problems);
            Assert.Equal(2, (await Categories(scope).GetAllAsync()).Count);
        }
    }

    [Theory]
    [InlineData("contact", "heeft al een andere betekenis")]
    [InlineData("spellen", "hoort al bij de categorie \"Spellen\"")]
    [InlineData("Spel len", "mag alleen kleine letters")]
    public async Task A_new_category_with_a_reserved_duplicate_or_invalid_slug_is_refused(string slug, string problem)
    {
        var (scope, user) = await NewTenantAsync("categorie-slug-" + slug.Length + slug[0]);
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).CreateAsync([new("nl", "Nieuw", slug)], user));

            Assert.Contains(problem, Assert.Single(refused.Problems));
            Assert.Equal(2, (await Categories(scope).GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task A_category_gets_its_own_name_slug_and_intro_in_one_language()
    {
        var (scope, user) = await NewTenantAsync("categorie-categorie");
        await using (scope)
        {
            var texts = Texts(scope);
            var castles = (await texts.GetAsync("nl")).Categories[0].CategoryId;

            await Categories(scope).SaveAsync(castles, "nl", "Luchtkastelen", "luchtkastelen", "Springen!\r\n\r\nVeilig.", user);

            var category = (await texts.GetAsync("nl")).Categories[0];
            Assert.Equal(("Luchtkastelen", "luchtkastelen", "Springen!\n\nVeilig."), (category.Name.Value, category.Slug.Value, category.Intro.Value));
            Assert.True(category.Slug.IsCustomized);
            Assert.False((await texts.GetAsync("fr")).Categories[0].Name.IsCustomized);

            await Categories(scope).ResetAsync(castles, "nl", WebsiteTextKey.CategorySlug);
            category = (await texts.GetAsync("nl")).Categories[0];
            Assert.Equal(new WebsiteText("springkastelen", false), category.Slug);
            Assert.True(category.Name.IsCustomized);
        }
    }

    [Fact]
    public async Task A_category_slug_is_unique_per_language()
    {
        var (scope, user) = await NewTenantAsync("categorie-slug");
        await using (scope)
        {
            var texts = Texts(scope);
            var categories = (await texts.GetAsync("nl")).Categories;
            var games = categories[1];

            var refused = await Assert.ThrowsAsync<CategoryException>(() =>
                Categories(scope).SaveAsync(games.CategoryId, "nl", games.Name.Value, "springkastelen", "", user));
            Assert.Equal(["De slug \"springkastelen\" hoort al bij de categorie \"Springkastelen\"; kies een andere."], refused.Problems);

            // In een andere taal mag dezelfde slug wel.
            await Categories(scope).SaveAsync(games.CategoryId, "en", "Games", "springkastelen", "", user);

            // Teruggaan naar een standaardslug die intussen bij een andere categorie hoort, kan niet.
            await Categories(scope).SaveAsync(games.CategoryId, "nl", games.Name.Value, "spelletjes", "", user);
            await Categories(scope).SaveAsync(categories[0].CategoryId, "nl", "Springkastelen", "spellen", "", user);
            await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).ResetAsync(games.CategoryId, "nl", WebsiteTextKey.CategorySlug));
        }
    }

    [Fact]
    public async Task Two_categories_whose_names_give_the_same_slug_still_get_different_slugs()
    {
        var (scope, _) = await NewTenantAsync("categorie-dubbel");
        await using (scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Categories.Add(new Category { Slug = "extra-spellen", SortOrder = 3, Translations = [T("nl", "Spellen!")] });
            await db.SaveChangesAsync();

            var slugs = (await Texts(scope).GetAsync("nl")).Categories.Select(c => c.Slug.Value);

            Assert.Equal(["springkastelen", "spellen", "extra-spellen"], slugs);
        }
    }

    [Fact]
    public async Task Two_administrators_saving_the_same_slug_at_once_cannot_both_win()
    {
        var (scope, user) = await NewTenantAsync("categorie-tegelijk");
        await using (scope)
        {
            var categories = (await Texts(scope).GetAsync("nl")).Categories;
            await using var other = await app.TenantScopeAsync("categorie-tegelijk");

            var results = await Task.WhenAll(
                Attempt(Categories(scope).SaveAsync(categories[0].CategoryId, "nl", "Een", "feest", "", user)),
                Attempt(Categories(other).SaveAsync(categories[1].CategoryId, "nl", "Twee", "feest", "", user)));

            Assert.Equal(1, results.Count(saved => saved));
            Assert.Single((await Texts(scope).GetAsync("nl")).Categories, c => c.Slug.Value == "feest");
        }

        static async Task<bool> Attempt(Task save)
        {
            try { await save; return true; }
            catch (CategoryException) { return false; }
        }
    }

    [Theory]
    [InlineData("Spring kastelen", "mag alleen kleine letters")]
    [InlineData("contact", "heeft al een andere betekenis")]
    public async Task An_invalid_slug_is_refused(string slug, string problem)
    {
        var (scope, user) = await NewTenantAsync("categorie-ongeldig-" + slug.Length);
        await using (scope)
        {
            var texts = Texts(scope);
            var castles = (await texts.GetAsync("nl")).Categories[0].CategoryId;

            var refused = await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).SaveAsync(castles, "nl", "Kastelen", slug, "", user));

            Assert.Contains(problem, Assert.Single(refused.Problems));
        }
    }

    [Fact]
    public async Task The_order_of_the_categories_is_the_order_on_the_website()
    {
        var (scope, user) = await NewTenantAsync("categorie-volgorde");
        await using (scope)
        {
            var categories = Categories(scope);
            var water = await categories.CreateAsync([new("nl", "Waterspellen")], user);
            var all = (await categories.GetAllAsync()).Select(c => c.Id).ToList();
            foreach (var (id, slug) in all.Zip(["kasteel", "spel", "waterglijbaan"])) await AddItemAsync(scope, id, slug);

            await categories.ReorderAsync([water, all[0], all[1]]);

            Assert.Equal([water, all[0], all[1]], (await categories.GetAllAsync()).Select(c => c.Id));
        }
        var html = await app.CreateClient("categorie-volgorde.test").GetStringAsync("/");

        var tiles = new[] { "Waterspellen", "Springkastelen", "Spellen" }
            .Select(name => html.IndexOf($"<span class=\"category-tile-name\">{name}</span>", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, tiles);
        Assert.Equal(tiles.Order(), tiles);
    }

    [Fact]
    public async Task Reordering_with_a_list_that_misses_a_category_is_refused()
    {
        var (scope, _) = await NewTenantAsync("categorie-volgorde-oud");
        await using (scope)
        {
            var all = (await Categories(scope).GetAllAsync()).Select(c => c.Id).ToList();

            await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).ReorderAsync([all[1]]));

            Assert.Equal(all, (await Categories(scope).GetAllAsync()).Select(c => c.Id));
        }
    }

    [Fact]
    public async Task An_empty_category_is_deleted_and_its_page_disappears()
    {
        var (scope, user) = await NewTenantAsync("categorie-verwijderen");
        await using (scope)
        {
            var categories = Categories(scope);
            var water = await categories.CreateAsync([new("nl", "Waterspellen")], user);
            await categories.SaveAsync(water, "nl", "Waterpret", "waterpret", "Nat!", user);

            await categories.DeleteAsync(water);

            Assert.DoesNotContain(water, (await categories.GetAllAsync()).Select(c => c.Id));
        }
        var client = app.CreateClient("categorie-verwijderen.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/waterpret")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/waterspellen")).StatusCode);
    }

    [Fact]
    public async Task A_category_with_items_cannot_be_deleted_even_if_they_are_switched_off()
    {
        var (scope, _) = await NewTenantAsync("categorie-niet-leeg");
        await using (scope)
        {
            var castles = (await Categories(scope).GetAllAsync())[0].Id;
            await AddItemAsync(scope, castles, "kasteel");
            await AddItemAsync(scope, castles, "oud-kasteel", isActive: false);

            var refused = await Assert.ThrowsAsync<CategoryException>(() => Categories(scope).DeleteAsync(castles));

            Assert.Equal(["De categorie \"Springkastelen\" bevat nog 2 artikelen. Zet die artikelen eerst in een andere categorie of verwijder ze."], refused.Problems);
            var kept = (await Categories(scope).GetAllAsync())[0];
            Assert.Equal((castles, 2), (kept.Id, kept.ItemCount));
        }
    }

    [Fact]
    public async Task An_old_category_slug_redirects_permanently_to_the_current_one_per_language()
    {
        var (scope, user) = await NewTenantAsync("categorie-doorsturen");
        await using (scope)
        {
            var castles = (await Categories(scope).GetAllAsync())[0].Id;
            await AddItemAsync(scope, castles);
            await Categories(scope).SaveAsync(castles, "fr", "Gonflables", "gonflables", "", user);
            await Categories(scope).SaveAsync(castles, "fr", "Gonflables", "structures-gonflables", "", user);
        }
        var client = app.CreateClient("categorie-doorsturen.test");

        foreach (var old in new[] { "/fr/chateaux-gonflables", "/fr/gonflables" })
        {
            var response = await client.GetAsync(old + "?van=2030-06-01&tot=2030-06-02");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal("/fr/structures-gonflables?van=2030-06-01&tot=2030-06-02", response.Headers.Location?.OriginalString);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/fr/structures-gonflables")).StatusCode);
        // De andere talen houden hun slug.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/springkastelen")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/en/bouncy-castles")).StatusCode);
    }

    [Fact]
    public async Task An_old_slug_that_another_category_takes_shows_that_category()
    {
        var (scope, user) = await NewTenantAsync("categorie-oude-slug-hergebruikt");
        await using (scope)
        {
            var all = await Categories(scope).GetAllAsync();
            await Categories(scope).SaveAsync(all[0].Id, "nl", "Luchtkastelen", "luchtkastelen", "", user);
            await Categories(scope).SaveAsync(all[1].Id, "nl", "Springkastelen", "springkastelen", "", user);
            await Categories(scope).SaveAsync(all[0].Id, "nl", "Kastelen", "kastelen", "", user);
            foreach (var (category, slug) in all.Zip(["kasteel", "spel"])) await AddItemAsync(scope, category.Id, slug);
        }
        var client = app.CreateClient("categorie-oude-slug-hergebruikt.test");

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/springkastelen"));
        Assert.Contains("<h1>Springkastelen</h1>", html);
        Assert.Contains("Kasteel", await client.GetStringAsync("/kastelen"));
        var old = await client.GetAsync("/luchtkastelen");
        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal("/kastelen", old.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Administrator_finds_the_categories_in_the_items_group_of_the_menu()
    {
        var client = await app.LoggedInClientAsync("categorieen-beheerder@hopsakee.test", UserRole.Administrator);

        var page = await client.GetAsync("/admin/categorieen");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"admin/categorieen\"", html);
        Assert.Contains("href=\"admin/artikelen\"", html);
        Assert.Contains("Nieuwe categorie", html);
    }

    [Fact]
    public async Task The_website_texts_no_longer_show_category_texts()
    {
        var client = await app.LoggedInClientAsync("categorieen-teksten@hopsakee.test", UserRole.Administrator);

        var html = await client.GetStringAsync("/admin/websiteteksten");

        Assert.DoesNotContain("Slug in de URL", html);
        Assert.DoesNotContain("Inleiding", html);
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_categories()
    {
        var client = await app.LoggedInClientAsync("categorieen-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/categorieen");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/categorieen\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    /// <summary>Een nieuwe verhuurder met de categorieën Springkastelen en Spellen, en een beheerder.</summary>
    private async Task<(AsyncServiceScope Scope, Guid User)> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        var user = await app.CreateUserAsync(slug, $"beheerder@{slug}.test");
        var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Categories.AddRange(
            new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] },
            new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen"), T("fr", "Jeux"), T("en", "Games")] });
        await db.SaveChangesAsync();
        return (scope, user.Id);
    }

    private static async Task AddItemAsync(AsyncServiceScope scope, Guid categoryId, string slug = "kasteel", bool isActive = true)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RentalItems.Add(new RentalItem
        {
            Slug = slug, CategoryId = categoryId, Stock = 1, IsActive = isActive, Pricing = new Pricing { DayPrice = 100 },
            Translations = [T("nl", "Kasteel"), T("fr", "Château"), T("en", "Castle")],
        });
        await db.SaveChangesAsync();
    }

    private static IWebsiteTexts Texts(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IWebsiteTexts>();

    private static ICategoryAdministration Categories(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ICategoryAdministration>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
