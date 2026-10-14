using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder uploadt tot 10 foto's per artikel, kiest hun volgorde (de eerste is de hoofdfoto) en verwijdert ze. De
/// reservatiewebsite toont ze verkleind, met de naam van het artikel als alt-tekst, in de lokale afbeeldingenopslag.
/// </summary>
public sealed partial class RentalItemPhotoTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    /// <summary>De categorie Springkastelen van de verhuurder uit <see cref="NewTenantAsync"/>.</summary>
    private Guid _castles;

    [Fact]
    public async Task Uploaded_photos_are_shown_resized_on_the_item_and_category_page_with_the_name_as_alt_text()
    {
        IReadOnlyList<string> photos;
        IImageStore images;
        await using (var scope = await NewTenantAsync("foto-upload"))
        {
            images = Images(scope);
            var id = await Items(scope).CreateAsync(Input("Jungle", "Jungle FR"));

            await Items(scope).AddPhotosAsync(id, [new("voorkant.jpg", TestImages.Jpeg(2400, 1800)), new("zijkant.png", TestImages.Png(1000, 750))]);

            photos = (await Items(scope).GetAsync(id))!.Photos;
            Assert.Equal(2, photos.Count);
            Assert.Equal(1600, TestImages.WidthOf(await GetBytesAsync(images.PublicUrl(photos[0], 1600))));

            var detail = await PageAsync("foto-upload", "/huren/jungle");
            Assert.Contains($"src=\"{images.PublicUrl(photos[0], 800)}\"", detail);
            Assert.Contains($"srcset=\"{images.SrcSet(photos[0])}\"", detail);
            Assert.Contains("alt=\"Jungle\"", detail);
            Assert.Contains($"src=\"{images.PublicUrl(photos[1], 800)}\"", detail);
            Assert.Contains("alt=\"Jungle FR\"", await PageAsync("foto-upload", "/fr/louer/jungle"));

            var category = await PageAsync("foto-upload", "/springkastelen");
            Assert.Contains($"srcset=\"{images.SrcSet(photos[0])}\"", category);
            Assert.DoesNotContain(images.PublicUrl(photos[1], 800), category);
        }
    }

    [Fact]
    public async Task At_most_ten_photos_per_item_are_accepted_and_a_refused_upload_keeps_none_of_its_photos()
    {
        await using var scope = await NewTenantAsync("foto-maximum");
        var id = await Items(scope).CreateAsync(Input("Jungle"));
        await Items(scope).AddPhotosAsync(id, Uploads(8));

        var tooMany = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).AddPhotosAsync(id, Uploads(3)));

        Assert.Equal(["Er kunnen maximaal 10 foto's bij: je kan er nog 2 toevoegen. Er werd geen enkele foto toegevoegd."], tooMany.Problems);
        Assert.Equal(8, (await Items(scope).GetAsync(id))!.Photos.Count);
        Assert.Equal(8, StoredImageCount(scope));

        await Items(scope).AddPhotosAsync(id, Uploads(2));
        var full = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).AddPhotosAsync(id, Uploads(1)));

        Assert.Equal(["Er kunnen maximaal 10 foto's bij; verwijder er eerst een."], full.Problems);
        Assert.Equal(10, (await Items(scope).GetAsync(id))!.Photos.Count);
    }

    [Fact]
    public async Task A_file_that_is_no_image_is_refused_by_name_and_the_other_photos_of_that_upload_are_not_kept()
    {
        await using var scope = await NewTenantAsync("foto-ongeldig");
        var id = await Items(scope).CreateAsync(Input("Jungle"));

        var refused = await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).AddPhotosAsync(id,
            [new("goed.jpg", TestImages.Jpeg(800, 600)), new("tekening.svg", new MemoryStream("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()))]));

        Assert.Equal([$"tekening.svg: {ImageUpload.WrongFormat} Er werd geen enkele foto toegevoegd."], refused.Problems);
        Assert.Empty((await Items(scope).GetAsync(id))!.Photos);
        Assert.Equal(0, StoredImageCount(scope));
    }

    [Fact]
    public async Task The_first_photo_after_reordering_is_the_main_photo_on_the_category_page_in_open_graph_and_in_json_ld()
    {
        IReadOnlyList<string> photos;
        await using (var scope = await NewTenantAsync("foto-volgorde"))
        {
            var id = await Items(scope).CreateAsync(Input("Jungle"));
            await Items(scope).AddPhotosAsync(id, Uploads(3));
            var uploaded = (await Items(scope).GetAsync(id))!.Photos;

            await Items(scope).ReorderPhotosAsync(id, [uploaded[2], uploaded[0], uploaded[1]]);

            photos = (await Items(scope).GetAsync(id))!.Photos;
            Assert.Equal([uploaded[2], uploaded[0], uploaded[1]], photos);
            await Assert.ThrowsAsync<RentalItemException>(() => Items(scope).ReorderPhotosAsync(id, [uploaded[0], uploaded[1]]));
        }
        var main = $"https://foto-volgorde.test/afbeeldingen/{photos[0]}";

        var category = await PageAsync("foto-volgorde", "/springkastelen");
        Assert.Contains($"src=\"{main}/800.webp\"", category);
        Assert.Contains($"<meta property=\"og:image\" content=\"{main}/1600.webp\" />", category);

        var detail = await PageAsync("foto-volgorde", "/huren/jungle");
        Assert.Contains($"<meta property=\"og:image\" content=\"{main}/1600.webp\" />", detail);
        var product = JsonLdScript().Matches(detail)
            .Select(m => JsonDocument.Parse(m.Groups[1].Value).RootElement)
            .Single(e => e.GetProperty("@type").GetString() == "Product");
        Assert.Equal(
            photos.Select(key => $"https://foto-volgorde.test/afbeeldingen/{key}/1600.webp"),
            product.GetProperty("image").EnumerateArray().Select(i => i.GetString()));
        Assert.True(detail.IndexOf($"{main}/800.webp", StringComparison.Ordinal) < detail.IndexOf($"{photos[1]}/800.webp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_deleted_photo_disappears_from_the_site_and_from_the_image_store()
    {
        await using var scope = await NewTenantAsync("foto-verwijderen");
        var images = Images(scope);
        var id = await Items(scope).CreateAsync(Input("Jungle"));
        await Items(scope).AddPhotosAsync(id, Uploads(2));
        var photos = (await Items(scope).GetAsync(id))!.Photos;

        await Items(scope).DeletePhotoAsync(id, photos[0]);

        Assert.Equal([photos[1]], (await Items(scope).GetAsync(id))!.Photos);
        foreach (var width in ImageUpload.Widths)
            Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(images.PublicUrl(photos[0], width)));
        var detail = await PageAsync("foto-verwijderen", "/huren/jungle");
        Assert.DoesNotContain(photos[0], detail);
        Assert.Contains($"srcset=\"{images.SrcSet(photos[1])}\"", detail);
    }

    [Fact]
    public async Task Deleting_an_item_removes_its_photos_from_the_image_store()
    {
        await using var scope = await NewTenantAsync("foto-artikel-weg");
        var id = await Items(scope).CreateAsync(Input("Jungle"));
        await Items(scope).AddPhotosAsync(id, Uploads(2));

        await Items(scope).DeleteAsync(id);

        Assert.Equal(0, StoredImageCount(scope));
    }

    [Fact]
    public async Task The_administrator_sees_the_photos_of_an_item_on_its_screen()
    {
        var client = await app.LoggedInClientAsync("fotos-beheerder@hopsakee.test", UserRole.Administrator);
        string photo;
        Guid id;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            id = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Fotokasteel")], null, 1, 100, null, NoSpecs, "fotokasteel"));
            await Items(scope).AddPhotosAsync(id, Uploads(1));
            photo = (await Items(scope).GetAsync(id))!.Photos[0];
        }

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/artikelen/{id}"));

        Assert.Contains("Foto's", html);
        Assert.Contains($"{photo}/400.webp", html);
        Assert.Contains("Hoofdfoto", html);
    }

    /// <summary>Zoveel foto's om te uploaden.</summary>
    private static IReadOnlyList<PhotoUpload> Uploads(int count) =>
        Enumerable.Range(1, count).Select(i => new PhotoUpload($"foto-{i}.jpg", TestImages.Jpeg(400 + i, 300))).ToList();

    /// <summary>Hoeveel afbeeldingen de verhuurder van deze scope in de afbeeldingenopslag heeft.</summary>
    private int StoredImageCount(AsyncServiceScope scope) =>
        app.StoredImageCount(scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant!.Id);

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdScript();

    private async Task<byte[]> GetBytesAsync(string url)
    {
        var uri = new Uri(url);
        var response = await app.CreateClient(uri.Host).GetAsync(uri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<HttpStatusCode> StatusOfAsync(string url)
    {
        var uri = new Uri(url);
        return (await app.CreateClient(uri.Host).GetAsync(uri.PathAndQuery)).StatusCode;
    }

    private async Task<string> PageAsync(string tenantSlug, string path) =>
        WebUtility.HtmlDecode(await app.CreateClient($"{tenantSlug}.test").GetStringAsync(path));

    /// <summary>Een nieuwe verhuurder met de categorie Springkastelen, waarin <see cref="Input"/> de artikelen zet.</summary>
    private async Task<AsyncServiceScope> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category
        {
            Slug = "springkastelen", SortOrder = 1,
            Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")],
        };
        db.Categories.Add(castles);
        await db.SaveChangesAsync();
        _castles = castles.Id;
        return scope;
    }

    private RentalItemInput Input(string name, string? frenchName = null) =>
        new(frenchName is null ? [new("nl", name)] : [new("nl", name), new("fr", frenchName)],
            _castles, 1, 100, null, NoSpecs);

    private static IRentalItemAdministration Items(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static IImageStore Images(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IImageStore>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
