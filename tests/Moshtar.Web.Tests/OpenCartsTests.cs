using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Carts;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Beheerder en medewerker zien in het back office de winkelkarren die nog niet gereserveerd zijn, zodat ze een
/// klant zelf kunnen contacteren. Alleen lezen, alleen van de eigen verhuurder.
/// </summary>
public sealed class OpenCartsTests(MoshtarApp app) : IClassFixture<MoshtarApp>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        foreach (var tenant in new[] { "hopsakee", "andere" })
        {
            await using var scope = await app.TenantScopeAsync(tenant);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.RentalItems.AnyAsync(i => i.Slug == "prinses")) continue;
            db.RentalItems.Add(new RentalItem
            {
                Slug = "prinses", Stock = 20, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 },
                Translations = [new Translation { Culture = "nl", Name = "Prinsessenkasteel" }],
            });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Staff)]
    public async Task Administrator_and_staff_see_the_open_carts_but_not_reserved_ones_or_those_of_another_company(UserRole role)
    {
        var name = role.ToString().ToLowerInvariant();
        var day = TestReservations.FutureDay(50);
        var query = $"?van={day:yyyy-MM-dd}&tot={day.AddDays(1):yyyy-MM-dd}";
        var anonymous = app.CreateClient(MoshtarApp.HopsakeeHost);
        await AddAsync(anonymous, "/huren/prinses" + query);
        var known = app.CreateClient(MoshtarApp.HopsakeeHost);
        await AddAsync(known, "/huren/prinses" + query, quantity: 2);
        await IdentifyAsync(known, $"open-{name}@example.test", reminder: true);
        var reserved = app.CreateClient(MoshtarApp.HopsakeeHost);
        await AddAsync(reserved, "/huren/prinses" + query);
        await IdentifyAsync(reserved, $"gereserveerd-{name}@example.test");
        (await ReserveAsync(reserved, "Rik")).EnsureSuccessStatusCode();
        var other = app.CreateClient(MoshtarApp.AndereHost);
        await AddAsync(other, "/huren/prinses" + query);
        await IdentifyAsync(other, $"andere-{name}@example.test");

        var client = await app.LoggedInClientAsync($"winkelkarren-{role}@hopsakee.test", role);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/winkelkarren"));

        Assert.Contains("href=\"admin/winkelkarren\"", html);
        Assert.Contains($"open-{name}@example.test", html);
        // 2 prinsessenkastelen voor 2 dagen: 2 × (120 + 60).
        Assert.Contains("2 × Prinsessenkasteel", html);
        Assert.Contains("€ 360,00", html);
        Assert.Contains("Anoniem", html);
        Assert.DoesNotContain($"gereserveerd-{name}@example.test", html);
        Assert.DoesNotContain($"andere-{name}@example.test", html);
    }

    [Fact]
    public async Task The_list_shows_when_the_reminder_was_sent_newest_cart_first()
    {
        var day = TestReservations.FutureDay(51);
        var first = app.CreateClient(MoshtarApp.HopsakeeHost);
        await AddAsync(first, $"/huren/prinses?van={day:yyyy-MM-dd}");
        await IdentifyAsync(first, "eerst@example.test", reminder: true);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<ICartHousekeeping>().RunAsync(DateTime.UtcNow.AddHours(25));
        var second = app.CreateClient(MoshtarApp.HopsakeeHost);
        await AddAsync(second, $"/huren/prinses?van={day:yyyy-MM-dd}");
        await IdentifyAsync(second, "daarna@example.test");

        await using var listScope = await app.TenantScopeAsync("hopsakee");
        var carts = await listScope.ServiceProvider.GetRequiredService<ICartAdministration>().ListOpenAsync();

        var emails = carts.Select(c => c.Email).Where(e => e is "eerst@example.test" or "daarna@example.test").ToList();
        Assert.Equal(["daarna@example.test", "eerst@example.test"], emails);
        Assert.NotNull(carts.Single(c => c.Email == "eerst@example.test").ReminderSentAtUtc);
        Assert.Null(carts.Single(c => c.Email == "daarna@example.test").ReminderSentAtUtc);
    }
}
