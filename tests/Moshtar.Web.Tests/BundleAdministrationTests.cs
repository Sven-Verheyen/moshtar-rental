using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Application.Reservations;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een beheerder maakt pakketten aan en wijzigt ze in het back office: naam en beschrijving per taal, artikelen met
/// aantallen, eigen prijs, slug, optioneel eigen foto's, uitzetten en verwijderen. Een pakket met een uitgezet artikel
/// staat niet op de reservatiewebsite.
/// </summary>
public sealed class BundleAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly ItemSpecs NoSpecs = new(null, null, null, null, null, null, false);

    [Fact]
    public async Task A_bundle_is_created_with_its_items_quantities_and_own_price_and_the_site_shows_it_in_every_language()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-aanmaken");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", " Kinderfeest XL ", "Alles voor een feest.")],
                [new(jungle, 1), new(cornhole, 2)], dayPrice: 199, extraDayPrice: 99));

            var created = await Bundles(scope).GetAsync(id);
            Assert.Equal("kinderfeest-xl", created!.Slug);
            Assert.True(created.IsActive);
            Assert.Equal([new BundleContent(jungle, 1), new BundleContent(cornhole, 2)], created.Input.Contents);
            Assert.Equal((199m, (decimal?)99m), (created.Input.DayPrice, created.Input.ExtraDayPrice));
            Assert.Equal("Kinderfeest XL", Assert.Single(created.Input.Texts, t => t.Culture == "nl").Name);
        }
        var client = app.CreateClient("pakket-aanmaken.test");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/huren/kinderfeest-xl"));
        Assert.Contains("<h1>Kinderfeest XL</h1>", html);
        Assert.Contains("1 × Jungle, 2 × Cornhole", html);
        Assert.Contains("199,00", html);
        // Zonder vertaling de naam en beschrijving in de standaardtaal.
        var french = await client.GetStringAsync("/fr/louer/kinderfeest-xl");
        Assert.Contains("<h1>Kinderfeest XL</h1>", french);
        Assert.Contains("Alles voor een feest.", french);
    }

    [Fact]
    public async Task A_bundle_without_a_name_in_the_default_language_or_without_items_is_refused()
    {
        var (scope, jungle, _) = await NewTenantAsync("pakket-ongeldig");
        await using (scope)
        {
            var refused = await Assert.ThrowsAsync<BundleException>(() =>
                Bundles(scope).CreateAsync(Input([new("nl", " "), new("fr", "Fête")], [], dayPrice: -1)));

            Assert.Equal(["De naam in het Nederlands is leeg.", "Een pakket bevat minstens één artikel.", "De dagprijs kan niet negatief zijn."],
                refused.Problems);

            refused = await Assert.ThrowsAsync<BundleException>(() =>
                Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1), new(jungle, 2)])));
            Assert.Equal(["Een artikel staat twee keer in het pakket; pas het aantal aan in plaats van het opnieuw toe te voegen."], refused.Problems);

            refused = await Assert.ThrowsAsync<BundleException>(() =>
                Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 0)])));
            Assert.Equal(["Het aantal van een artikel in het pakket is minstens 1."], refused.Problems);
            Assert.Empty(await Bundles(scope).GetAllAsync());
        }
    }

    [Fact]
    public async Task Items_are_added_to_and_removed_from_a_bundle_and_its_texts_and_prices_change()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-wijzigen");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));

            await Bundles(scope).SaveAsync(id, Input([new("nl", "Groot feest"), new("en", "Big party", "Everything.")],
                [new(cornhole, 2)], slug: "feest", dayPrice: 80, extraDayPrice: 40));

            var saved = (await Bundles(scope).GetAsync(id))!;
            Assert.Equal([new BundleContent(cornhole, 2)], saved.Input.Contents);
            Assert.Equal((80m, (decimal?)40m, "feest"), (saved.Input.DayPrice, saved.Input.ExtraDayPrice, saved.Slug));
            Assert.Equal(["fr"], Assert.Single(await Bundles(scope).GetAllAsync()).MissingTranslations);

            await Bundles(scope).SaveAsync(id, Input([new("nl", "Groot feest")], [new(cornhole, 1), new(jungle, 1)], slug: "feest"));
            Assert.Equal([new BundleContent(jungle, 1), new BundleContent(cornhole, 1)], (await Bundles(scope).GetAsync(id))!.Input.Contents);
        }
        var client = app.CreateClient("pakket-wijzigen.test");
        Assert.Contains("<h1>Groot feest</h1>", await client.GetStringAsync("/en/rent/feest"));
        Assert.Contains("1 × Jungle, 1 × Cornhole", WebUtility.HtmlDecode(await client.GetStringAsync("/huren/feest")));
    }

    [Fact]
    public async Task A_bundle_slug_is_unique_among_items_and_bundles()
    {
        var (scope, jungle, _) = await NewTenantAsync("pakket-slug");
        await using (scope)
        {
            await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            var second = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            Assert.Equal("feest-2", (await Bundles(scope).GetAsync(second))!.Slug);
            // Het artikel Jungle heeft de slug "jungle".
            var fromItem = await Bundles(scope).CreateAsync(Input([new("nl", "Jungle")], [new(jungle, 1)]));
            Assert.Equal("jungle-2", (await Bundles(scope).GetAsync(fromItem))!.Slug);

            foreach (var taken in new[] { "jungle", "feest" })
            {
                var refused = await Assert.ThrowsAsync<BundleException>(() =>
                    Bundles(scope).SaveAsync(second, Input([new("nl", "Feest")], [new(jungle, 1)], slug: taken)));
                Assert.Equal([$"De slug \"{taken}\" hoort al bij een ander artikel of pakket; kies een andere."], refused.Problems);
            }
            var itemRefused = await Assert.ThrowsAsync<RentalItemException>(() =>
                Items(scope).SaveAsync(jungle, new RentalItemInput([new("nl", "Jungle")], null, 1, 100, null, NoSpecs, "feest-2")));
            Assert.Contains("hoort al bij een ander artikel of pakket", Assert.Single(itemRefused.Problems));
        }
    }

    [Fact]
    public async Task An_old_bundle_slug_redirects_permanently_to_the_new_one_until_an_item_takes_it()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-doorsturen");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            await Bundles(scope).SaveAsync(id, Input([new("nl", "Feest")], [new(jungle, 1)], slug: "kinderfeest"));
            var renamed = await Bundles(scope).CreateAsync(Input([new("nl", "Oud")], [new(cornhole, 1)]));
            await Bundles(scope).SaveAsync(renamed, Input([new("nl", "Oud")], [new(cornhole, 1)], slug: "nieuw"));
            // Een artikel neemt de oude slug van een pakket: het huidige adres gaat voor.
            await Items(scope).SaveAsync(cornhole, new RentalItemInput([new("nl", "Cornhole")], null, 2, 30, null, NoSpecs, "oud"));
        }
        var client = app.CreateClient("pakket-doorsturen.test");

        foreach (var (old, current) in new[] { ("/huren/feest", "/huren/kinderfeest"), ("/fr/louer/feest", "/fr/louer/kinderfeest") })
        {
            var response = await client.GetAsync(old + "?van=2030-06-01&tot=2030-06-02");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal(current + "?van=2030-06-01&tot=2030-06-02", response.Headers.Location?.OriginalString);
        }
        Assert.Contains("<h1>Cornhole</h1>", await client.GetStringAsync("/huren/oud"));
    }

    [Fact]
    public async Task A_bundle_without_own_photos_shows_the_photos_of_its_items_and_with_own_photos_only_those()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-fotos");
        await using (scope)
        {
            var images = scope.ServiceProvider.GetRequiredService<IImageStore>();
            await Items(scope).AddPhotosAsync(jungle, Uploads(1));
            await Items(scope).AddPhotosAsync(cornhole, Uploads(1));
            var itemPhotos = new[] { (await Items(scope).GetAsync(jungle))!.Photos[0], (await Items(scope).GetAsync(cornhole))!.Photos[0] };
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1), new(cornhole, 1)]));

            var page = await PageAsync("pakket-fotos", "/huren/feest");
            Assert.Contains($"srcset=\"{images.SrcSet(itemPhotos[0])}\"", page);
            Assert.Contains($"srcset=\"{images.SrcSet(itemPhotos[1])}\"", page);

            await Bundles(scope).AddPhotosAsync(id, Uploads(2));
            var own = (await Bundles(scope).GetAsync(id))!.Photos;
            await Bundles(scope).ReorderPhotosAsync(id, [own[1], own[0]]);
            Assert.Equal([own[1], own[0]], (await Bundles(scope).GetAsync(id))!.Photos);

            page = await PageAsync("pakket-fotos", "/huren/feest");
            Assert.Contains($"src=\"{images.PublicUrl(own[1], 800)}\"", page);
            Assert.True(page.IndexOf(own[1], StringComparison.Ordinal) < page.IndexOf(own[0], StringComparison.Ordinal));
            Assert.Contains("alt=\"Feest\"", page);
            Assert.DoesNotContain(itemPhotos[0], page);

            await Bundles(scope).DeletePhotoAsync(id, own[0]);
            Assert.Equal([own[1]], (await Bundles(scope).GetAsync(id))!.Photos);
            Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(images.PublicUrl(own[0], 800)));
        }
    }

    [Fact]
    public async Task At_most_ten_own_photos_per_bundle_are_accepted_and_deleting_the_bundle_removes_them()
    {
        var (scope, jungle, _) = await NewTenantAsync("pakket-fotos-maximum");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            await Bundles(scope).AddPhotosAsync(id, Uploads(9));

            var tooMany = await Assert.ThrowsAsync<BundleException>(() => Bundles(scope).AddPhotosAsync(id, Uploads(2)));

            Assert.Equal(["Er kunnen maximaal 10 foto's bij: je kan er nog 1 toevoegen. Er werd geen enkele foto toegevoegd."], tooMany.Problems);
            Assert.Equal(9, StoredImageCount(scope));

            await Bundles(scope).DeleteAsync(id);
            Assert.Equal(0, StoredImageCount(scope));
        }
    }

    [Fact]
    public async Task A_bundle_with_a_switched_off_item_is_hidden_and_cannot_be_reserved_and_the_back_office_says_why()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-artikel-uit");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1), new(cornhole, 1)]));
            await Items(scope).SetActiveAsync(cornhole, false);

            var summary = Assert.Single(await Bundles(scope).GetAllAsync());
            Assert.True(summary.IsActive);
            Assert.False(summary.IsVisible);
            Assert.Equal(["Cornhole"], summary.SwitchedOffItems);
            Assert.Equal(["Cornhole"], (await Bundles(scope).GetAsync(id))!.SwitchedOffItems);
            var refused = await Reservations(scope).ReserveAsync(Request(TestReservations.FutureDay(), new ReservationLineRequest(null, id, 1)), ReservationActor.Website);
            Assert.Null(refused.Reservation);
        }
        var client = app.CreateClient("pakket-artikel-uit.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/feest")).StatusCode);
        Assert.DoesNotContain(">Feest</a>", await client.GetStringAsync("/"));

        await using (scope = await app.TenantScopeAsync("pakket-artikel-uit"))
            await Items(scope).SetActiveAsync(cornhole, true);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/huren/feest")).StatusCode);
    }

    [Fact]
    public async Task A_switched_off_bundle_disappears_and_cannot_be_reserved_but_existing_reservations_stay()
    {
        var (scope, jungle, _) = await NewTenantAsync("pakket-uitzetten");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)], dayPrice: 120));
            var reserved = await Reservations(scope).ReserveAsync(Request(TestReservations.FutureDay(), new ReservationLineRequest(null, id, 1)), ReservationActor.Website);

            await Bundles(scope).SetActiveAsync(id, false);

            Assert.False((await Bundles(scope).GetAsync(id))!.IsActive);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Reservations(scope).ReserveAsync(Request(TestReservations.FutureDay(3), new ReservationLineRequest(null, id, 1)), ReservationActor.Website));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var kept = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reserved.Reservation!.Id);
            Assert.Equal((ReservationStatus.Confirmed, 120m, (Guid?)id), (kept.Status, kept.TotalPrice, kept.Lines.Single().BundleId));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient("pakket-uitzetten.test").GetAsync("/huren/feest")).StatusCode);
    }

    [Fact]
    public async Task A_bundle_that_was_never_reserved_is_deleted_and_its_old_addresses_disappear()
    {
        var (scope, jungle, _) = await NewTenantAsync("pakket-verwijderen");
        await using (scope)
        {
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            await Bundles(scope).SaveAsync(id, Input([new("nl", "Feest")], [new(jungle, 1)], slug: "kinderfeest"));

            await Bundles(scope).DeleteAsync(id);

            Assert.Null(await Bundles(scope).GetAsync(id));
            // Het artikel kan nu weer verwijderd worden: geen pakket verwijst er nog naar.
            await Items(scope).DeleteAsync(jungle);
        }
        var client = app.CreateClient("pakket-verwijderen.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/feest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/huren/kinderfeest")).StatusCode);
    }

    [Fact]
    public async Task A_reserved_bundle_or_one_in_a_cart_cannot_be_deleted_and_switching_off_is_proposed()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-niet-verwijderen");
        Guid inCart;
        await using (scope)
        {
            var reserved = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1)]));
            await Reservations(scope).ReserveAsync(Request(TestReservations.FutureDay(), new ReservationLineRequest(null, reserved, 1)), ReservationActor.Website);
            inCart = await Bundles(scope).CreateAsync(Input([new("nl", "Spelpakket")], [new(cornhole, 2)]));

            var refused = await Assert.ThrowsAsync<BundleException>(() => Bundles(scope).DeleteAsync(reserved));
            Assert.True(refused.CanSwitchOff);
            Assert.Equal(["Het pakket \"Feest\" werd al gereserveerd en kan niet verwijderd worden. Zet het uit, dan verdwijnt het van je reservatiewebsite en blijven de reservaties ongewijzigd."], refused.Problems);
        }
        var client = app.CreateClient("pakket-niet-verwijderen.test");
        var day = TestReservations.FutureDay(5).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Redirect, (await WebsiteCart.AddAsync(client, $"/huren/spelpakket?van={day}&tot={day}")).StatusCode);

        await using (scope = await app.TenantScopeAsync("pakket-niet-verwijderen"))
        {
            var refused = await Assert.ThrowsAsync<BundleException>(() => Bundles(scope).DeleteAsync(inCart));
            Assert.True(refused.CanSwitchOff);
            Assert.Contains("ligt in de winkelkar van een klant", Assert.Single(refused.Problems));
            Assert.Equal(2, (await Bundles(scope).GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task The_availability_of_a_bundle_follows_from_its_items()
    {
        var (scope, jungle, cornhole) = await NewTenantAsync("pakket-beschikbaarheid");
        await using (scope)
        {
            // Cornhole heeft voorraad 2: een pakket met 2 Cornhole kan niet als er al één gereserveerd is.
            var id = await Bundles(scope).CreateAsync(Input([new("nl", "Feest")], [new(jungle, 1), new(cornhole, 2)]));
            var day = TestReservations.FutureDay();
            await Reservations(scope).ReserveAsync(Request(day, new ReservationLineRequest(cornhole, null, 1)), ReservationActor.Website);

            Assert.Contains(id, await Reservations(scope).FindUnavailableAsync(new DateRange(day, day)));
            Assert.DoesNotContain(id, await Reservations(scope).FindUnavailableAsync(new DateRange(day.AddDays(7), day.AddDays(7))));

            var bundled = await Reservations(scope).ReserveAsync(Request(day.AddDays(7), new ReservationLineRequest(null, id, 1)), ReservationActor.Website);
            Assert.NotNull(bundled.Reservation);
            var jungleAlone = await Reservations(scope).ReserveAsync(Request(day.AddDays(7), new ReservationLineRequest(jungle, null, 1)), ReservationActor.Website);
            Assert.Null(jungleAlone.Reservation);
        }
    }

    [Fact]
    public async Task Administrator_sees_the_bundles_and_why_one_is_not_on_the_site()
    {
        var client = await app.LoggedInClientAsync("pakketten-beheerder@hopsakee.test", UserRole.Administrator);
        Guid id;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            var item = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Pakketkasteel")], null, 1, 100, null, NoSpecs, "pakketkasteel"));
            id = await Bundles(scope).CreateAsync(Input([new("nl", "Overzichtspakket")], [new(item, 1)], slug: "overzichtspakket"));
            await Items(scope).SetActiveAsync(item, false);
        }

        var page = await client.GetAsync("/admin/pakketten");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Overzichtspakket", html);
        Assert.Contains("1 × Pakketkasteel", html);
        Assert.Contains("Niet op je reservatiewebsite: Pakketkasteel is uitgezet.", html);
        Assert.Contains("Nieuw pakket", html);
        Assert.Contains("href=\"admin/pakketten\"", html);
        var editor = WebUtility.HtmlDecode(await client.GetStringAsync($"/admin/pakketten/{id}"));
        Assert.Contains("Niet op je reservatiewebsite: Pakketkasteel is uitgezet.", editor);
        Assert.Contains("Foto's", editor);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/pakketten/nieuw")).StatusCode);
    }

    [Theory]
    [InlineData("/admin/pakketten")]
    [InlineData("/admin/pakketten/nieuw")]
    public async Task Staff_cannot_manage_bundles(string path)
    {
        var client = await app.LoggedInClientAsync($"pakketten-medewerker-{path.Length}@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static ReservationRequest Request(DateOnly start, ReservationLineRequest line) => new(
        new DateRange(start, start),
        [line],
        new ReservationCustomer("Test", "Klant", "klant@example.test", "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
        DeliveryMethod.Pickup, null, null, "nl");

    /// <summary>Zoveel foto's om te uploaden.</summary>
    private static IReadOnlyList<PhotoUpload> Uploads(int count) =>
        Enumerable.Range(1, count).Select(i => new PhotoUpload($"foto-{i}.jpg", TestImages.Jpeg(400 + i, 300))).ToList();

    private int StoredImageCount(AsyncServiceScope scope) =>
        app.StoredImageCount(scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant!.Id);

    private async Task<HttpStatusCode> StatusOfAsync(string url)
    {
        var uri = new Uri(url);
        return (await app.CreateClient(uri.Host).GetAsync(uri.PathAndQuery)).StatusCode;
    }

    private async Task<string> PageAsync(string tenantSlug, string path) =>
        WebUtility.HtmlDecode(await app.CreateClient($"{tenantSlug}.test").GetStringAsync(path));

    private static IReservationService Reservations(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IReservationService>();

    private static BundleInput Input(IReadOnlyList<RentalItemText> texts, IReadOnlyList<BundleContent> contents, string? slug = null,
        decimal dayPrice = 150, decimal? extraDayPrice = null) =>
        new(texts, contents, dayPrice, extraDayPrice, slug);

    /// <summary>Een nieuwe verhuurder met de artikelen Jungle (voorraad 1) en Cornhole (voorraad 2) in de categorie Springkastelen.</summary>
    private async Task<(AsyncServiceScope Scope, Guid Jungle, Guid Cornhole)> NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var castles = new Category { Slug = "springkastelen", SortOrder = 1, Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")] };
        db.Categories.Add(castles);
        await db.SaveChangesAsync();
        var jungle = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Jungle")], castles.Id, 1, 100, null, NoSpecs));
        var cornhole = await Items(scope).CreateAsync(new RentalItemInput([new("nl", "Cornhole")], castles.Id, 2, 30, null, NoSpecs));
        return (scope, jungle, cornhole);
    }

    private static IBundleAdministration Bundles(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IBundleAdministration>();

    private static IRentalItemAdministration Items(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRentalItemAdministration>();

    private static Translation T(string culture, string name) => new() { Culture = culture, Name = name };
}
