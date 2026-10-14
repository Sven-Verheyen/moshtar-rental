using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Images;

namespace Moshtar.Web.Tests;

/// <summary>
/// De afbeeldingenopslag (ADR 0005), hier de lokale opslag: een upload wordt verkleind, is via een volledig https-adres
/// publiek bereikbaar en verdwijnt na verwijderen. Elke verhuurder heeft zijn eigen afbeeldingen.
/// </summary>
public sealed class ImageStoreTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task An_upload_is_resized_to_fixed_widths_and_publicly_reachable()
    {
        await app.CreateTenantAsync("afbeelding-upload", "afbeelding-upload.test");
        await using var scope = await app.TenantScopeAsync("afbeelding-upload");
        var images = Images(scope);

        var key = await images.UploadAsync(TestImages.Png(2000, 1000));

        var medium = images.PublicUrl(key, 800);
        Assert.StartsWith("https://afbeelding-upload.test/afbeeldingen/", medium);
        var response = await GetAsync(medium);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(800, TestImages.WidthOf(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(1600, TestImages.WidthOf(await (await GetAsync(images.PublicUrl(key, 1600))).Content.ReadAsByteArrayAsync()));
        Assert.Equal(400, TestImages.WidthOf(await (await GetAsync(images.PublicUrl(key, 100))).Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task A_small_image_is_never_enlarged()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var images = Images(scope);

        var key = await images.UploadAsync(TestImages.Jpeg(300, 200));

        var response = await GetAsync(images.PublicUrl(key, 1600));
        Assert.Equal(300, TestImages.WidthOf(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task A_deleted_image_is_gone()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var images = Images(scope);
        var key = await images.UploadAsync(TestImages.Png(500, 500));

        await images.DeleteAsync(key);

        foreach (var width in ImageUpload.Widths)
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(images.PublicUrl(key, width))).StatusCode);
    }

    [Fact]
    public async Task Only_common_image_formats_are_accepted()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");

        var refused = await Assert.ThrowsAsync<ImageRejectedException>(() =>
            Images(scope).UploadAsync(new MemoryStream("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray())));

        Assert.Equal("Dit bestand is geen afbeelding in een gangbaar formaat (JPG, PNG, WebP of GIF).", refused.Message);
    }

    [Fact]
    public async Task An_image_above_the_maximum_size_is_refused()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var tooLarge = new MemoryStream(new byte[ImageUpload.MaxBytes + 1]);

        var refused = await Assert.ThrowsAsync<ImageRejectedException>(() => Images(scope).UploadAsync(tooLarge));

        Assert.Equal("Deze afbeelding is te groot: maximaal 10 MB.", refused.Message);
    }

    [Fact]
    public async Task One_rental_company_cannot_delete_the_images_of_another()
    {
        string key;
        await using (var hopsakee = await app.TenantScopeAsync("hopsakee"))
            key = await Images(hopsakee).UploadAsync(TestImages.Png(500, 500));
        string url;
        await using (var hopsakee = await app.TenantScopeAsync("hopsakee"))
            url = Images(hopsakee).PublicUrl(key, 400);

        await using (var andere = await app.TenantScopeAsync("andere"))
            await Assert.ThrowsAsync<ImageRejectedException>(() => Images(andere).DeleteAsync(key));

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Every_upload_gets_its_own_key_so_nothing_is_overwritten()
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var images = Images(scope);

        var first = await images.UploadAsync(TestImages.Png(500, 500));
        var second = await images.UploadAsync(TestImages.Png(600, 600));

        Assert.NotEqual(first, second);
        Assert.Equal(500, TestImages.WidthOf(await (await GetAsync(images.PublicUrl(first, 1600))).Content.ReadAsByteArrayAsync()));
    }

    /// <summary>Het adres opvragen zoals een browser, op de webapp in de test.</summary>
    private Task<HttpResponseMessage> GetAsync(string url)
    {
        var uri = new Uri(url);
        return app.CreateClient(uri.Host).GetAsync(uri.PathAndQuery);
    }

    private static IImageStore Images(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IImageStore>();
}
