using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Infrastructure.Identity;
using Moshtar.Web.Tenancy;
using static Moshtar.Web.Tests.ReviewTenant;

namespace Moshtar.Web.Tests;

/// <summary>
/// Staat "Reviewvragen sturen" aan, dan toont de dagweergave van de Planning een blok "Reviewvragen" met de klanten van
/// wie de huurperiode 1 tot 3 dagen eerder eindigde, met een WhatsApp-knop. De reservaties liggen in de toekomst; de
/// app draait met een klok die ernaartoe gezet wordt.
/// </summary>
public sealed class ReviewWhatsAppTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task The_day_after_the_end_day_the_planning_shows_the_customer_with_a_whatsapp_button_with_the_review_request()
    {
        await CreateAsync(app, "wa-review");
        var end = TestReservations.FutureDay(1);
        var reservation = await ReserveAsync(app, "wa-review", "sara@example.test", "Sara", end);
        await using var later = Later(At(end.AddDays(1), 9, 0));
        var client = await ClientAsync(later.App, "wa-review", "wa-review@example.test", UserRole.Staff);

        var block = Section(await PlanningAsync(client, end.AddDays(1)), "reviewvragen");

        Assert.Contains("Reviewvragen", block);
        Assert.Contains("Sara Klant", block);
        var link = Link(block);
        Assert.StartsWith("https://wa.me/32470123456?text=", link);
        var message = Uri.UnescapeDataString(link[(link.IndexOf("text=") + 5)..]);
        Assert.Contains("Sara", message);
        Assert.Contains(ReviewLink, message);
        Assert.DoesNotContain("{", message);
        Assert.Contains("target=\"_blank\"", block[block.IndexOf("wa.me")..]);
        Assert.Contains(await NumberAsync("wa-review", reservation), block);
    }

    [Fact]
    public async Task A_customer_stays_in_the_block_from_one_to_three_days_after_the_end_day()
    {
        await CreateAsync(app, "wa-dagen");
        var end = TestReservations.FutureDay(2);
        await ReserveAsync(app, "wa-dagen", "dagen@example.test", "Dirk", end);
        await using var later = Later(At(end.AddDays(5), 9, 0));
        var client = await ClientAsync(later.App, "wa-dagen", "wa-dagen@example.test", UserRole.Staff);

        Assert.DoesNotContain("id=\"reviewvragen\"", await PlanningAsync(client, end));
        foreach (var after in new[] { 1, 2, 3 })
            Assert.Contains("Dirk Klant", Section(await PlanningAsync(client, end.AddDays(after)), "reviewvragen"));
        Assert.DoesNotContain("id=\"reviewvragen\"", await PlanningAsync(client, end.AddDays(4)));
    }

    [Fact]
    public async Task There_is_no_block_on_a_future_day_or_with_the_switch_off()
    {
        await CreateAsync(app, "wa-toekomst");
        await CreateAsync(app, "wa-uit", switchedOn: false);
        var end = TestReservations.FutureDay(3);
        await ReserveAsync(app, "wa-toekomst", "toekomst@example.test", "Tom", end);
        await ReserveAsync(app, "wa-uit", "uit@example.test", "Ulla", end);
        await using var later = Later(At(end.AddDays(1), 9, 0));
        var future = await ClientAsync(later.App, "wa-toekomst", "wa-toekomst@example.test", UserRole.Staff);
        var off = await ClientAsync(later.App, "wa-uit", "wa-uit@example.test", UserRole.Administrator);

        Assert.Contains("Tom Klant", Section(await PlanningAsync(future, end.AddDays(1)), "reviewvragen"));
        Assert.DoesNotContain("id=\"reviewvragen\"", await PlanningAsync(future, end.AddDays(2)));
        Assert.DoesNotContain("id=\"reviewvragen\"", await PlanningAsync(off, end.AddDays(1)));
        Assert.DoesNotContain("Reviewvragen", await PlanningAsync(off, end.AddDays(1)));
    }

    [Fact]
    public async Task The_block_follows_the_rules_of_the_mail()
    {
        var before = TestReservations.FutureDay(4);
        var end = before.AddDays(1);
        await CreateAsync(app, "wa-regels", since: At(end, 8, 0).ToUniversalTime());
        await ReserveAsync(app, "wa-regels", "oud@example.test", "Otto", before);
        var cancelled = await ReserveAsync(app, "wa-regels", "geannuleerd@example.test", "Gina", end);
        await ReserveAsync(app, "wa-regels", "afgemeld@example.test", "Arne", end);
        var first = await ReserveAsync(app, "wa-regels", "twee@example.test", "Tine", end);
        var second = await ReserveAsync(app, "wa-regels", "Twee@Example.test ", "Tine", end);
        await ReserveAsync(app, "wa-regels", "welkom@example.test", "Wout", end);
        await using (var scope = await app.TenantScopeAsync("wa-regels"))
        {
            await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(cancelled, ReservationStatus.Cancelled, ReservationActor.Website);
            await scope.ServiceProvider.GetRequiredService<IReviewOptOuts>().OptOutAsync("afgemeld@example.test");
        }
        await using var later = Later(At(end.AddDays(1), 9, 0));
        var client = await ClientAsync(later.App, "wa-regels", "wa-regels@example.test", UserRole.Staff);

        var block = Section(await PlanningAsync(client, end.AddDays(1)), "reviewvragen");

        Assert.Contains("Wout Klant", block);
        Assert.DoesNotContain("Otto", block);
        Assert.DoesNotContain("Gina", block);
        Assert.DoesNotContain("Arne", block);
        // Twee reservaties van dezelfde klant met dezelfde einddag: één knop, met beide nummers.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(block, "Tine Klant"));
        Assert.Contains($"{await NumberAsync("wa-regels", first)}, {await NumberAsync("wa-regels", second)}", block);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(block, "href=\"https://wa.me/").Count);
    }

    [Fact]
    public async Task A_customer_without_a_phone_number_is_listed_without_a_whatsapp_button()
    {
        await CreateAsync(app, "wa-geen-nummer");
        var end = TestReservations.FutureDay(5);
        var reservation = await ReserveAsync(app, "wa-geen-nummer", "geen-nummer@example.test", "Nele", end);
        await using (var scope = await app.TenantScopeAsync("wa-geen-nummer"))
        {
            // Een reservatie van voor het telefoonnummer verplicht was.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Reservations.Where(r => r.Id == reservation).Select(r => r.Customer!).SingleAsync()).Phone = null;
            await db.SaveChangesAsync();
        }
        await using var later = Later(At(end.AddDays(1), 9, 0));
        var client = await ClientAsync(later.App, "wa-geen-nummer", "wa-geen-nummer@example.test", UserRole.Staff);

        var block = Section(await PlanningAsync(client, end.AddDays(1)), "reviewvragen");

        Assert.Contains("Nele Klant", block);
        Assert.Contains("Geen gsm-nummer", block);
        Assert.DoesNotContain("wa.me", block);
    }

    [Fact]
    public async Task Pressing_the_whatsapp_button_is_remembered_shown_and_in_the_history_and_the_customer_leaves_the_block_the_next_day()
    {
        await CreateAsync(app, "wa-klik");
        var end = TestReservations.FutureDay(6);
        var first = await ReserveAsync(app, "wa-klik", "klik@example.test", "Karel", end);
        var second = await ReserveAsync(app, "wa-klik", "klik@example.test", "Karel", end);
        var other = await ReserveAsync(app, "wa-klik", "ander@example.test", "Anouk", end);
        await using var later = Later(At(end.AddDays(1), 10, 12));
        var client = await ClientAsync(later.App, "wa-klik", "wa-klik@example.test", UserRole.Administrator);
        var user = await app.CreateUserAsync("wa-klik", "wa-klik@example.test", UserRole.Administrator);

        await RecordAsync(later.App, "wa-klik", end.AddDays(1), second, ReservationActor.User(user.Id));
        later.Now = At(end.AddDays(1), 11, 0);
        await RecordAsync(later.App, "wa-klik", end.AddDays(1), first, ReservationActor.User(user.Id));

        var block = Section(await PlanningAsync(client, end.AddDays(1)), "reviewvragen");
        var karel = Card(block, await NumberAsync("wa-klik", first));
        Assert.Contains("WhatsApp op 10:12", karel);
        Assert.DoesNotContain("wa.me", karel);
        var anouk = Card(block, await NumberAsync("wa-klik", other));
        Assert.DoesNotContain("WhatsApp op", anouk);
        Assert.Contains("wa.me", anouk);

        foreach (var reservation in new[] { first, second })
        {
            var sent = Assert.Single(await HistoryAsync(app, "wa-klik", reservation), e => e.Kind == ReservationEventKind.ReviewRequestWhatsApp);
            Assert.Equal((At(end.AddDays(1), 10, 12).UtcDateTime, "wa-klik@example.test"), (sent.OccurredAtUtc, sent.UserEmail));
        }
        Assert.DoesNotContain(await HistoryAsync(app, "wa-klik", other), e => e.Kind == ReservationEventKind.ReviewRequestWhatsApp);

        later.Now = At(end.AddDays(2), 9, 0);
        var nextDay = Section(await PlanningAsync(client, end.AddDays(2)), "reviewvragen");
        Assert.DoesNotContain("Karel", nextDay);
        Assert.Contains("Anouk Klant", nextDay);
        var history = await PlanningAsync(client, end.AddDays(1));
        Assert.Contains("Karel", Section(history, "reviewvragen"));
    }

    [Fact]
    public async Task A_click_for_a_customer_who_is_not_in_the_block_of_that_day_is_refused()
    {
        await CreateAsync(app, "wa-weigeren");
        var end = TestReservations.FutureDay(7);
        var reservation = await ReserveAsync(app, "wa-weigeren", "weigeren@example.test", "Wim", end);
        await using var later = Later(At(end.AddDays(1), 9, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => RecordAsync(later.App, "wa-weigeren", end, reservation, ReservationActor.Website));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RecordAsync(later.App, "wa-weigeren", end.AddDays(2), reservation, ReservationActor.Website));
        Assert.DoesNotContain(await HistoryAsync(app, "wa-weigeren", reservation), e => e.Kind == ReservationEventKind.ReviewRequestWhatsApp);
    }

    [Fact]
    public async Task The_whatsapp_message_is_in_the_language_of_the_reservation_and_follows_the_customized_template()
    {
        await CreateAsync(app, "wa-taal");
        var end = TestReservations.FutureDay(8);
        await ReserveAsync(app, "wa-taal", "camille@example.test", "Camille", end, culture: "fr");
        await ReserveAsync(app, "wa-taal", "greet@example.test", "Greet", end);
        var user = await app.CreateUserAsync("wa-taal", "wa-taal@example.test", UserRole.Administrator);
        await using (var scope = await app.TenantScopeAsync("wa-taal"))
            await scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>().SaveAsync(MailKind.ReviewRequest, "nl",
                MailKinds.ReviewRequest.Standard("nl") with { WhatsApp = "Dag {firstName}, een review? {reviewLink}" }, user.Id);
        await using var later = Later(At(end.AddDays(1), 9, 0));

        await using var at = await ScopeAsync(later.App, "wa-taal");
        var requests = await at.ServiceProvider.GetRequiredService<IReviewWhatsApp>().ForDayAsync(end.AddDays(1));

        Assert.Equal(
            Uri.EscapeDataString($"Bonjour Camille, merci d'avoir loué chez wa-taal ! Pourriez-vous écrire un avis sur votre expérience ? C'est ici : {ReviewLink}"),
            Text(requests.Single(r => r.CustomerName == "Camille Klant").WhatsAppLink!));
        Assert.Equal($"https://wa.me/32470123456?text={Uri.EscapeDataString($"Dag Greet, een review? {ReviewLink}")}",
            requests.Single(r => r.CustomerName == "Greet Klant").WhatsAppLink);
    }

    // --- Hulpmiddelen ---

    private static string Text(string link) => link[(link.IndexOf("text=") + 5)..];

    private static async Task RecordAsync(WebApplicationFactory<Program> at, string tenantSlug, DateOnly day, Guid reservationId, ReservationActor actor)
    {
        await using var scope = await ScopeAsync(at, tenantSlug);
        await scope.ServiceProvider.GetRequiredService<IReviewWhatsApp>().RecordAsync(day, reservationId, actor);
    }

    /// <summary>Het kaartje van één klant in het blok, tot het volgende kaartje.</summary>
    private static string Card(string section, string number)
    {
        var start = section.IndexOf($"data-reservation=\"{number}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Reservatie {number} niet gevonden.");
        var end = section.IndexOf("data-reservation=", start + 1, StringComparison.Ordinal);
        return end < 0 ? section[start..] : section[start..end];
    }

    /// <summary>De app met een klok die op dit moment staat en die de test kan verzetten.</summary>
    private LaterApp Later(DateTimeOffset now)
    {
        var clock = new MovableClock(now);
        return new LaterApp(app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock))), clock);
    }

    private static async Task<AsyncServiceScope> ScopeAsync(WebApplicationFactory<Program> at, string tenantSlug)
    {
        var scope = at.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Hosts).SingleAsync(t => t.Slug == tenantSlug);
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
        return scope;
    }

    private async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> at, string tenantSlug, string email, UserRole role)
    {
        await app.CreateUserAsync(tenantSlug, email, role);
        var client = at.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{tenantSlug}.test"), AllowAutoRedirect = false });
        var login = await BackOffice.LoginAsync(client, email, MoshtarApp.Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private async Task<string> NumberAsync(string tenantSlug, Guid reservationId)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.Where(r => r.Id == reservationId).Select(r => r.Number).SingleAsync();
    }

    private static async Task<string> PlanningAsync(HttpClient client, DateOnly day)
    {
        var response = await client.GetAsync($"/admin/planning?dag={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Het stuk van de pagina vanaf de sectie met deze id tot de volgende sectie (of het einde).</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Sectie {id} niet gevonden.");
        var end = html.IndexOf("<section", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    /// <summary>De eerste wa.me-link in dit stuk van de pagina.</summary>
    private static string Link(string html)
    {
        var start = html.IndexOf("href=\"https://wa.me/", StringComparison.Ordinal);
        Assert.True(start >= 0, "Geen WhatsApp-knop gevonden.");
        start += "href=\"".Length;
        return html[start..html.IndexOf('"', start)];
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class LaterApp(WebApplicationFactory<Program> app, MovableClock clock) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> App => app;
        public DateTimeOffset Now { get => clock.Now; set => clock.Now = value; }
        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
