using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Carts;
using Moshtar.Application.Mail;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.WebsiteCart;

namespace Moshtar.Web.Tests;

/// <summary>
/// Wie bij het reserveren een herinnering vroeg en niet reserveerde, krijgt 24 uur na de laatste wijziging één
/// mail met een link die de winkelkar opent, ook op een ander toestel. Winkelkarren die 7 dagen niet gewijzigd en
/// niet gereserveerd werden, verdwijnen. Het onderhoud loopt hier met een vaste klok, zonder de achtergrondtaak.
/// </summary>
public sealed class CartReminderTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly TimeSpan JustOverADay = TimeSpan.FromHours(25);

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task After_a_day_the_customer_gets_one_reminder_whose_link_opens_the_cart_on_another_device()
    {
        await NewTenantAsync("herinnering");
        var day = TestReservations.FutureDay(1);
        await StartCartAsync("herinnering", "Sara@Example.test", day, reminder: true);

        Assert.Equal(new CartHousekeepingResult(0, 0), await RunAsync("herinnering", DateTime.UtcNow.AddHours(23)));
        Assert.Equal(new CartHousekeepingResult(1, 0), await RunAsync("herinnering", DateTime.UtcNow + JustOverADay));
        Assert.Equal(new CartHousekeepingResult(0, 0), await RunAsync("herinnering", DateTime.UtcNow + JustOverADay + TimeSpan.FromHours(1)));

        var mail = Assert.Single(Outbox.Sent, m => m.To == "sara@example.test");
        Assert.Equal(("noreply@herinnering.test", "herinnering", "info@herinnering.test"), (mail.FromAddress, mail.FromName, mail.ReplyTo));
        Assert.Equal("Je winkelkar bij herinnering staat nog klaar", mail.Subject);
        Assert.Contains("1 × Prinsessenkasteel – € 120,00", mail.TextBody);
        Assert.Contains(day.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("nl-BE")), mail.TextBody);
        var link = Regex.Match(mail.TextBody, @"Naar je winkelkar: (https://herinnering\.test/winkelkar/herinnering/\S+)").Groups[1].Value;
        Assert.Contains($"href=\"{link}\"", mail.HtmlBody);

        // Een ander toestel, zonder cookie: de link zet de cookie en opent de winkelkar.
        var phone = app.CreateClient("herinnering.test");
        var opened = await phone.GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal("/winkelkar", opened.Headers.Location!.OriginalString);
        Assert.Contains(opened.Headers.GetValues("Set-Cookie"), c => c.StartsWith("moshtar-winkelkar="));
        var cart = await PageAsync(phone, "/winkelkar");
        Assert.Contains("Prinsessenkasteel", cart);
        Assert.Contains("<strong>sara@example.test</strong>", cart);
    }

    [Fact]
    public async Task The_reminder_is_in_the_language_of_the_customer_and_its_link_opens_the_cart_in_that_language()
    {
        await NewTenantAsync("herinnering-fr");
        await StartCartAsync("herinnering-fr", "camille@example.test", TestReservations.FutureDay(2), reminder: true, cartPath: "/fr/panier");

        await RunAsync("herinnering-fr", DateTime.UtcNow + JustOverADay);

        var mail = Assert.Single(Outbox.Sent, m => m.To == "camille@example.test");
        Assert.Equal("Votre panier chez herinnering-fr vous attend", mail.Subject);
        var link = Regex.Match(mail.TextBody, @"Voir votre panier: (\S+)").Groups[1].Value;
        var opened = await app.CreateClient("herinnering-fr.test").GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal("/fr/panier", opened.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task No_reminder_without_the_checkbox_after_reserving_when_the_period_is_over_or_when_nothing_is_free()
    {
        await NewTenantAsync("geen-herinnering");
        var day = TestReservations.FutureDay(3);
        await StartCartAsync("geen-herinnering", "zonder-vinkje@example.test", day, reminder: false);
        var reserved = await StartCartAsync("geen-herinnering", "gereserveerd@example.test", TestReservations.FutureDay(4), reminder: true);
        (await ReserveAsync(reserved, "Ria")).EnsureSuccessStatusCode();
        await StartCartAsync("geen-herinnering", "morgen@example.test", DateOnly.FromDateTime(DateTime.Today).AddDays(1), reminder: true);
        await StartCartAsync("geen-herinnering", "niet-vrij@example.test", day, reminder: true);
        await ReserveForSomeoneElseAsync("geen-herinnering", day);

        // Drie dagen later: de huurperiode van morgen is voorbij, de winkelkarren zijn nog geen 7 dagen oud.
        Assert.Equal(new CartHousekeepingResult(0, 0), await RunAsync("geen-herinnering", DateTime.UtcNow.AddDays(3)));
        Assert.DoesNotContain(Outbox.Sent, m => m.To.EndsWith("@example.test") && m.Subject.Contains("winkelkar"));
    }

    [Fact]
    public async Task After_seven_days_without_a_change_a_cart_that_was_not_reserved_is_deleted_with_its_email_address()
    {
        await NewTenantAsync("opruimen");
        var left = await StartCartAsync("opruimen", "weg@example.test", TestReservations.FutureDay(5), reminder: true);
        var reserved = await StartCartAsync("opruimen", "blijft@example.test", TestReservations.FutureDay(6), reminder: false);
        (await ReserveAsync(reserved, "Bart")).EnsureSuccessStatusCode();

        Assert.Equal(0, (await RunAsync("opruimen", DateTime.UtcNow.AddDays(6))).CartsDeleted);
        Assert.Equal(new CartHousekeepingResult(0, 1), await RunAsync("opruimen", DateTime.UtcNow.AddDays(7).AddMinutes(1)));

        await using var scope = await app.TenantScopeAsync("opruimen");
        var carts = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Carts.AsNoTracking().ToListAsync();
        Assert.Equal(["blijft@example.test"], carts.Select(c => c.Email));
        Assert.Contains("Je winkelkar is leeg.", await PageAsync(left, "/winkelkar"));
    }

    [Fact]
    public async Task The_reminder_text_can_be_changed_but_keeps_its_link_and_has_a_preview()
    {
        await NewTenantAsync("herinnering-sjabloon");
        await using var scope = await app.TenantScopeAsync("herinnering-sjabloon");
        var templates = scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>();

        var error = await Assert.ThrowsAsync<MailTemplateException>(() =>
            templates.PreviewAsync(MailKind.CartReminder, "nl", new MailTemplateText("Nog even", "Je winkelkar wacht.")));
        Assert.Equal(["{cartLink} ontbreekt in de tekst."], error.Problems);

        var preview = await templates.PreviewAsync(MailKind.CartReminder, "en", new MailTemplateText("Still there, {companyName}?", "Look: {cartLink}"));
        Assert.Equal("Still there, herinnering-sjabloon?", preview.Subject);
        Assert.Contains("Go to your cart: https://herinnering-sjabloon.test/winkelkar/herinnering/voorbeeld", preview.TextBody);
    }

    [Fact]
    public async Task The_privacy_statement_explains_the_cart()
    {
        await NewTenantAsync("herinnering-privacy");
        var html = await PageAsync(app.CreateClient("herinnering-privacy.test"), "/privacy");
        Assert.Contains("Je winkelkar", html);
        Assert.Contains("7 dagen", html);
    }

    /// <summary>Een klant legt het prinsessenkasteel in zijn winkelkar en doet stap 1. Geeft zijn browser.</summary>
    private async Task<HttpClient> StartCartAsync(string tenant, string email, DateOnly day, bool reminder, string cartPath = "/winkelkar")
    {
        var client = app.CreateClient($"{tenant}.test");
        var prefix = cartPath.StartsWith("/fr/") ? "/fr/louer" : "/huren";
        var added = await AddAsync(client, $"{prefix}/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        Assert.EndsWith("winkelkar=toegevoegd&aantal=1#reserveren", added.Headers.Location!.OriginalString);
        await IdentifyAsync(client, email, reminder: reminder, cartPath: cartPath);
        return client;
    }

    private async Task<CartHousekeepingResult> RunAsync(string tenant, DateTime nowUtc)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<ICartHousekeeping>().RunAsync(nowUtc);
    }

    private async Task ReserveForSomeoneElseAsync(string tenant, DateOnly day)
    {
        var client = app.CreateClient($"{tenant}.test");
        await AddAsync(client, $"/huren/prinses?van={day:yyyy-MM-dd}&tot={day:yyyy-MM-dd}");
        await IdentifyAsync(client, "iemand-anders@example.test");
        (await ReserveAsync(client, "Anders")).EnsureSuccessStatusCode();
    }

    private async Task NewTenantAsync(string slug)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RentalItems.Add(new RentalItem
        {
            Slug = "prinses", Stock = 1, Pricing = new Pricing { DayPrice = 120, ExtraDayPrice = 60 },
            Translations = [new Translation { Culture = "nl", Name = "Prinsessenkasteel" }, new Translation { Culture = "fr", Name = "Château princesse" }],
        });
        await db.SaveChangesAsync();
    }
}
