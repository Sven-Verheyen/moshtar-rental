using System.Net;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

public sealed class RolePermissionTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Staff_sees_only_reservations_in_the_menu()
    {
        var client = await app.LoggedInClientAsync("menu-medewerker@hopsakee.test", UserRole.Staff);

        var page = await client.GetStringAsync("/admin/reservaties");

        Assert.Contains("href=\"admin/reservaties\"", page);
        Assert.DoesNotContain("href=\"admin/artikelen\"", page);
    }

    [Fact]
    public async Task Staff_gets_no_access_to_the_items_page()
    {
        var client = await app.LoggedInClientAsync("artikelen-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/artikelen");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Geen toegang", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_can_open_the_reservations()
    {
        var client = await app.LoggedInClientAsync("reservaties-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/reservaties");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_reaches_every_page_and_sees_the_full_menu()
    {
        var client = await app.LoggedInClientAsync("alles-beheerder@hopsakee.test", UserRole.Administrator);

        var items = await client.GetAsync("/admin/artikelen");
        var reservations = await client.GetStringAsync("/admin/reservaties");

        Assert.Equal(HttpStatusCode.OK, items.StatusCode);
        Assert.Contains("href=\"admin/artikelen\"", reservations);
    }
}
