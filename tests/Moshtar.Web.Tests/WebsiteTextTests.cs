using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Tenancy;
using Moshtar.Application.Website;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Website;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Site;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder past de teksten van zijn reservatiewebsite aan: titel en ondertitel van de home en de veelgestelde
/// vragen. De teksten van de categorieën staan bij <see cref="CategoryAdministrationTests"/>. Zonder aanpassing gelden de standaardteksten van Moshtar.
/// </summary>
public sealed class WebsiteTextTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Without_customization_the_standard_texts_apply_with_the_name_and_service_area()
    {
        var (scope, _) = await NewTenantAsync("teksten-standaard", serviceArea: ["Mortsel", "Edegem"]);
        await using (scope)
        {
            var texts = await Texts(scope).GetAsync("fr");

            Assert.Equal(new WebsiteText("Location de châteaux gonflables et de jeux chez teksten-standaard", false), texts.HomeTitle);
            Assert.Equal("Réservez en ligne, nous livrons et installons tout à Mortsel et Edegem.", texts.HomeSubtitle.Value);
            Assert.Equal(5, texts.Faq.Count);
            Assert.False(texts.FaqIsCustomized);
            var castles = texts.Categories[0];
            Assert.Equal(new WebsiteText("Châteaux gonflables", false), castles.Name);
            Assert.Equal(new WebsiteText("chateaux-gonflables", false), castles.Slug);
            Assert.Equal(new WebsiteText("", false), castles.Intro);
        }
    }

    [Fact]
    public async Task Home_texts_are_customized_per_language_and_reset_per_text()
    {
        var (scope, user) = await NewTenantAsync("teksten-home");
        await using (scope)
        {
            var texts = Texts(scope);
            var standardSubtitle = (await texts.GetAsync("nl")).HomeSubtitle.Value;

            await texts.SaveHomeAsync("nl", " Feest! ", standardSubtitle, user);
            var dutch = await texts.GetAsync("nl");
            Assert.Equal(new WebsiteText("Feest!", true), dutch.HomeTitle);
            Assert.False(dutch.HomeSubtitle.IsCustomized);
            Assert.False((await texts.GetAsync("en")).HomeTitle.IsCustomized);

            await texts.ResetAsync("nl", WebsiteTextKey.HomeTitle);
            Assert.False((await texts.GetAsync("nl")).HomeTitle.IsCustomized);
        }
    }

    [Fact]
    public async Task An_empty_title_is_refused()
    {
        var (scope, user) = await NewTenantAsync("teksten-leeg");
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<WebsiteTextException>(() => Texts(scope).SaveHomeAsync("nl", " ", "Ondertitel", user));
            Assert.Equal(["De titel is leeg."], refused.Problems);
        }
    }

    [Fact]
    public void Every_fixed_word_of_the_website_is_reserved_for_category_slugs()
    {
        foreach (var route in new SiteRoute[] { SiteRoute.Contact, SiteRoute.Faq, SiteRoute.Privacy, SiteRoute.Cart, new RentalRoute("x") })
            foreach (var culture in new[] { "nl", "fr", "en" })
                Assert.Contains(route.PathIn(culture).Split('/')[0], CategorySlug.Reserved);
        // De pagina's die Blazor zelf toont.
        Assert.Contains("not-found", CategorySlug.Reserved);
        Assert.Contains("error", CategorySlug.Reserved);
        // De keuze over Google Analytics.
        Assert.Contains("toestemming", CategorySlug.Reserved);
    }

    [Fact]
    public async Task Faq_is_saved_in_order_and_reset_to_the_standard()
    {
        var (scope, user) = await NewTenantAsync("teksten-faq");
        await using (scope)
        {
            var texts = Texts(scope);

            await texts.SaveFaqAsync("en", [new("Second?", "Yes."), new(" First? ", " No. ")], user);
            var english = await texts.GetAsync("en");
            Assert.True(english.FaqIsCustomized);
            Assert.Equal([new FaqEntry("Second?", "Yes."), new FaqEntry("First?", "No.")], english.Faq);

            await texts.SaveFaqAsync("en", [], user);
            Assert.Empty((await texts.GetAsync("en")).Faq);

            await texts.ResetAsync("en", WebsiteTextKey.Faq);
            Assert.Equal(5, (await texts.GetAsync("en")).Faq.Count);

            var refused = await Assert.ThrowsAsync<WebsiteTextException>(() => texts.SaveFaqAsync("en", [new("Why?", "")], user));
            Assert.Equal(["Het antwoord op vraag 1 is leeg."], refused.Problems);
        }
    }

    [Fact]
    public async Task A_text_equal_to_the_standard_is_not_a_customization()
    {
        var (scope, user) = await NewTenantAsync("teksten-gelijk");
        await using (scope)
        {
            var texts = Texts(scope);
            var standard = await texts.GetAsync("nl");

            await texts.SaveHomeAsync("nl", standard.HomeTitle.Value, standard.HomeSubtitle.Value, user);
            await texts.SaveFaqAsync("nl", standard.Faq, user);

            var saved = await texts.GetAsync("nl");
            Assert.False(saved.HomeTitle.IsCustomized || saved.HomeSubtitle.IsCustomized || saved.FaqIsCustomized);
        }
    }

    [Fact]
    public async Task The_home_shows_the_title_and_subtitle_in_the_language_of_the_url()
    {
        var (scope, user) = await NewTenantAsync("teksten-site", "teksten-site.test");
        await using (scope)
        {
            await Texts(scope).SaveHomeAsync("fr", "Fête & châteaux", "Tout pour votre fête.", user);
            var castles = (await Texts(scope).GetAsync("fr")).Categories[0];
            await scope.ServiceProvider.GetRequiredService<ICategoryAdministration>().SaveAsync(castles.CategoryId, "fr", "Gonflables", castles.Slug.Value, "", user);
            await AddItemAsync(scope, castles.CategoryId);
        }
        var client = app.CreateClient("teksten-site.test");

        var french = WebUtility.HtmlDecode(await client.GetStringAsync("/fr"));
        var dutch = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("<h1>Fête & châteaux</h1>", french);
        Assert.Contains("<p>Tout pour votre fête.</p>", french);
        Assert.Contains("<meta name=\"description\" content=\"Tout pour votre fête.\" />", french);
        Assert.Contains("<span class=\"category-tile-name\">Gonflables</span>", french);
        Assert.Contains("<h1>Springkastelen en feestspellen huren bij teksten-site</h1>", dutch);
        Assert.Contains("<span class=\"category-tile-name\">Springkastelen</span>", dutch);
    }

    [Theory]
    [InlineData("/veelgestelde-vragen", "nl", "Veelgestelde vragen", "Vragen", "Wat als het regent?")]
    [InlineData("/fr/questions-frequentes", "fr", "Questions fréquentes", "FAQ", "Et s'il pleut ?")]
    [InlineData("/en/faq", "en", "FAQ", "FAQ", "What if it rains?")]
    public async Task The_faq_page_shows_the_questions_in_every_language(string path, string culture, string title, string menu, string question)
    {
        var response = await app.CreateClient(MoshtarApp.HopsakeeHost).GetAsync(path);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\">", html);
        Assert.Contains($"<h1>{title}</h1>", html);
        Assert.Contains($"<summary>{question}</summary>", html);
        Assert.Contains($"<a href=\"{path}\" aria-current=\"page\">{menu}</a>", html);
    }

    [Fact]
    public async Task The_faq_page_shows_the_customized_questions_with_paragraphs()
    {
        var (scope, user) = await NewTenantAsync("teksten-faqpagina", "teksten-faqpagina.test");
        await using (scope)
            await Texts(scope).SaveFaqAsync("nl", [new("Mag de hond mee?", "Nee.\n\nWel op de foto.")], user);

        var html = await app.CreateClient("teksten-faqpagina.test").GetStringAsync("/veelgestelde-vragen");

        Assert.Contains("<summary>Mag de hond mee?</summary>", html);
        Assert.Contains("<p>Nee.</p><p>Wel op de foto.</p>", html);
        Assert.DoesNotContain("regent", html);
    }

    [Fact]
    public async Task Texts_of_one_rental_company_never_appear_at_another()
    {
        var (one, user) = await NewTenantAsync("teksten-een", "teksten-een.test");
        await using (one)
        {
            await Texts(one).SaveHomeAsync("nl", "Geheime titel", "Geheime ondertitel", user);
            await Texts(one).SaveFaqAsync("nl", [new("Geheime vraag?", "Ja.")], user);
        }
        var (two, _) = await NewTenantAsync("teksten-twee", "teksten-twee.test");
        await using (two)
            Assert.False((await Texts(two).GetAsync("nl")).HomeTitle.IsCustomized);

        var client = app.CreateClient("teksten-twee.test");
        foreach (var path in new[] { "/", "/veelgestelde-vragen" })
            Assert.DoesNotContain("Geheim", await client.GetStringAsync(path));
    }

    [Fact]
    public async Task Administrator_finds_the_website_texts_in_the_menu()
    {
        var client = await app.LoggedInClientAsync("teksten-menu@hopsakee.test", UserRole.Administrator);

        var page = await client.GetAsync("/admin/websiteteksten");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"admin/websiteteksten\"", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_website_texts()
    {
        var client = await app.LoggedInClientAsync("teksten-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/websiteteksten");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/websiteteksten\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    /// <summary>Een nieuwe verhuurder met de categorieën Springkastelen en Spellen, en een beheerder.</summary>
    private async Task<(AsyncServiceScope Scope, Guid User)> NewTenantAsync(string slug, string? host = null, string[]? serviceArea = null)
    {
        await app.CreateTenantAsync(slug, host ?? $"{slug}.test");
        var user = await app.CreateUserAsync(slug, $"beheerder@{slug}.test");
        var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (serviceArea is not null)
        {
            var tenant = db.Tenants.Single(t => t.Slug == slug);
            tenant.ServiceArea = [.. serviceArea];
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant!.ServiceArea = [.. serviceArea];
        }
        db.Categories.AddRange(
            new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] },
            new Category { Slug = "spellen", SortOrder = 2, Translations = [T("nl", "Spellen"), T("fr", "Jeux"), T("en", "Games")] });
        await db.SaveChangesAsync();
        return (scope, user.Id);
    }

    private static async Task AddItemAsync(AsyncServiceScope scope, Guid categoryId)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RentalItems.Add(new RentalItem
        {
            Slug = "kasteel", CategoryId = categoryId, Stock = 1, Pricing = new Pricing { DayPrice = 100 },
            Translations = [T("nl", "Kasteel"), T("fr", "Château"), T("en", "Castle")],
        });
        await db.SaveChangesAsync();
    }

    private static IWebsiteTextAdministration Texts(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IWebsiteTextAdministration>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
