using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Domain.Marketing;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;
using static Moshtar.Web.Tests.ReviewTenant;

namespace Moshtar.Web.Tests;

/// <summary>
/// Onderaan elke reviewvraag staat "Ik wil geen reviewvragen meer". De klant komt op een pagina van de
/// reservatiewebsite in zijn taal, bevestigt zonder in te loggen en krijgt bij die verhuurder geen reviewvragen meer.
/// </summary>
public sealed class ReviewOptOutTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task The_opt_out_link_in_the_review_request_stops_review_requests_from_that_rental_company_only()
    {
        await CreateAsync(app, "afmelden");
        await CreateAsync(app, "afmelden-ander");
        var end = TestReservations.FutureDay(20);
        await ReserveAsync(app, "afmelden", "Wim@example.test", "Wim", end);
        await RunAsync(app, "afmelden", At(end.AddDays(1), 10, 0));

        var mail = Assert.Single(Mails(app, "wim@example.test"));
        var link = OptOutLink(mail.TextBody);
        Assert.StartsWith("https://afmelden.test/geen-reviewvragen?", link);
        Assert.Contains($"href=\"{WebUtility.HtmlEncode(link)}\"", mail.HtmlBody);
        Assert.Contains(">Ik wil geen reviewvragen meer</a>", mail.HtmlBody);

        var confirmation = await OptOutAsync("afmelden.test", link);
        Assert.Contains("Je krijgt van afmelden geen reviewvragen meer.", confirmation);

        await ReserveAsync(app, "afmelden", "wim@example.test", "Wim", end.AddDays(5));
        await ReserveAsync(app, "afmelden-ander", "wim@example.test", "Wim", end.AddDays(5));
        Assert.Equal(0, await RunAsync(app, "afmelden", At(end.AddDays(6), 10, 0)));
        Assert.Equal(1, await RunAsync(app, "afmelden-ander", At(end.AddDays(6), 10, 0)));
        Assert.Single(Mails(app, "wim@example.test"), m => m.Subject.Contains("afmelden-ander"));
    }

    [Theory]
    [InlineData("fr", "/fr/plus-de-demandes-d-avis?", "Je ne veux plus de demandes d'avis", "Vous ne recevrez plus de demandes d'avis de afmelden-taal-fr.")]
    [InlineData("en", "/en/no-review-requests?", "I don't want review requests anymore", "You will no longer get review requests from afmelden-taal-en.")]
    public async Task The_opt_out_page_is_in_the_language_of_the_review_request(string culture, string path, string linkText, string confirmation)
    {
        var tenant = $"afmelden-taal-{culture}";
        await CreateAsync(app, tenant);
        var end = TestReservations.FutureDay(23);
        await ReserveAsync(app, tenant, $"taal-{culture}@example.test", "Toon", end, culture);
        await RunAsync(app, tenant, At(end.AddDays(1), 10, 0));

        var mail = Assert.Single(Mails(app, $"taal-{culture}@example.test"));
        var link = Regex.Match(mail.TextBody, Regex.Escape(linkText) + @": (\S+)").Groups[1].Value;
        Assert.StartsWith($"https://{tenant}.test{path}", link);

        var page = await WebsiteCart.PageAsync(app.CreateClient($"{tenant}.test"), new Uri(link).PathAndQuery);
        Assert.Contains($"<html lang=\"{culture}\"", page);
        Assert.Contains($"{linkText}</button>", page);
        Assert.Contains(confirmation, await OptOutAsync($"{tenant}.test", link));
    }

    [Fact]
    public async Task A_changed_token_or_a_token_on_another_rental_companys_site_opts_nobody_out()
    {
        await CreateAsync(app, "afmelden-vals");
        await CreateAsync(app, "afmelden-vals-ander");
        var end = TestReservations.FutureDay(21);
        await ReserveAsync(app, "afmelden-vals", "vals@example.test", "Vic", end);
        await ReserveAsync(app, "afmelden-vals-ander", "vals@example.test", "Vic", end);
        await RunAsync(app, "afmelden-vals", At(end.AddDays(1), 10, 0));
        var link = OptOutLink(Assert.Single(Mails(app, "vals@example.test")).TextBody);
        var path = new Uri(link).PathAndQuery;
        var token = Uri.UnescapeDataString(path[(path.IndexOf("code=") + 5)..]);
        var changed = path.Replace(Uri.EscapeDataString(token), Uri.EscapeDataString((token[0] == 'A' ? 'B' : 'A') + token[1..]));

        foreach (var (host, wrong) in new[] { ("afmelden-vals.test", changed), ("afmelden-vals.test", "/geen-reviewvragen"), ("afmelden-vals-ander.test", path) })
        {
            var client = app.CreateClient(host);
            var page = await WebsiteCart.PageAsync(client, wrong);
            Assert.Contains("Deze link werkt niet.", page);
            Assert.DoesNotContain("Ik wil geen reviewvragen meer</button>", page);
            // Zonder knop op de pagina: het formulier zelf nagemaakt, met een geldig antiforgery-veld van een andere pagina.
            // Blazor weigert het (400), want de pagina toont geen formulier "afmelden".
            var form = await WebsiteCart.PageAsync(client, "/admin/uitnodiging");
            var post = await WebsiteCart.PostAsync(client, wrong, form, new() { ["_handler"] = "afmelden" });
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            Assert.DoesNotContain("geen reviewvragen meer.", WebUtility.HtmlDecode(await post.Content.ReadAsStringAsync()));
        }

        await ReserveAsync(app, "afmelden-vals", "vals@example.test", "Vic", end.AddDays(5));
        Assert.Equal(1, await RunAsync(app, "afmelden-vals", At(end.AddDays(6), 10, 0)));
        Assert.Equal(1, await RunAsync(app, "afmelden-vals-ander", At(end.AddDays(1), 10, 0)));
    }

    [Fact]
    public async Task Opting_out_changes_nothing_about_reservation_mails_or_the_marketing_contact()
    {
        await CreateAsync(app, "afmelden-rest");
        await using (var scope = await app.TenantScopeAsync("afmelden-rest"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MarketingContacts.Add(new MarketingContact { Email = "rest@example.test", Culture = "nl", ConsentedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var end = TestReservations.FutureDay(22);
        await ReserveAsync(app, "afmelden-rest", "rest@example.test", "Roos", end);
        await RunAsync(app, "afmelden-rest", At(end.AddDays(1), 10, 0));

        await OptOutAsync("afmelden-rest.test", OptOutLink(Assert.Single(Mails(app, "rest@example.test")).TextBody));
        await ReserveAsync(app, "afmelden-rest", "rest@example.test", "Roos", end.AddDays(5));

        var outbox = app.Services.GetRequiredService<RecordingMailTransport>().Sent;
        Assert.Equal(2, outbox.Count(m => m.To == "rest@example.test" && m.Subject.StartsWith("Bevestiging van je reservatie")));
        await using (var scope = await app.TenantScopeAsync("afmelden-rest"))
            Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().MarketingContacts.AnyAsync(c => c.Email == "rest@example.test"));
    }

    [Fact]
    public async Task The_back_office_shows_opted_out_customers_at_their_reservations()
    {
        await CreateAsync(app, "afmelden-bo");
        var end = TestReservations.FutureDay(24);
        await ReserveAsync(app, "afmelden-bo", "weg@example.test", "Wout", end);
        await ReserveAsync(app, "afmelden-bo", "blijft@example.test", "Bert", end);
        await RunAsync(app, "afmelden-bo", At(end.AddDays(1), 10, 0));
        await OptOutAsync("afmelden-bo.test", OptOutLink(Assert.Single(Mails(app, "weg@example.test")).TextBody));

        await app.CreateUserAsync("afmelden-bo", "medewerker@afmelden-bo.test", UserRole.Staff);
        var client = app.CreateClient("afmelden-bo.test");
        await BackOffice.LoginAsync(client, "medewerker@afmelden-bo.test", MoshtarApp.Password);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/admin/reservaties"));

        Assert.Equal(1, WebsiteCart.Count(html, "Afgemeld voor reviewvragen"));
        Assert.Matches(@"weg@example\.test</small>\s*<br\s*/?>\s*<small[^>]*>Afgemeld voor reviewvragen", html);
    }

    [Theory]
    [InlineData("/privacy", "<h2>Reviewvragen</h2>", "‘Ik wil geen reviewvragen meer’", "via WhatsApp sturen, naar het telefoonnummer")]
    [InlineData("/fr/confidentialite", "<h2>Demandes d'avis</h2>", "« Je ne veux plus de demandes d'avis »", "par WhatsApp, au numéro de téléphone")]
    [InlineData("/en/privacy", "<h2>Review requests</h2>", "‘I don't want review requests anymore’", "via WhatsApp, to the phone number")]
    public async Task The_privacy_statement_explains_review_requests_and_how_to_opt_out_when_they_are_switched_on(string path, string heading, string optOut, string whatsApp)
    {
        var slug = "afmelden-privacy-" + path.Split('/')[1];
        await CreateAsync(app, slug);
        await CreateAsync(app, slug + "-uit", switchedOn: false);

        var on = WebUtility.HtmlDecode(await app.CreateClient($"{slug}.test").GetStringAsync(path));
        var off = WebUtility.HtmlDecode(await app.CreateClient($"{slug}-uit.test").GetStringAsync(path));

        Assert.Contains(heading, on);
        Assert.Contains(optOut, on);
        Assert.Contains(whatsApp, on);
        Assert.Contains($"{slug} ", on[on.IndexOf(heading)..]);
        Assert.DoesNotContain(heading, off);
    }

    [Fact]
    public async Task An_opt_out_link_keeps_working_after_a_restart_because_the_keys_are_in_the_database()
    {
        await CreateAsync(app, "afmelden-herstart");
        var end = TestReservations.FutureDay(26);
        await ReserveAsync(app, "afmelden-herstart", "herstart@example.test", "Hanne", end);
        await RunAsync(app, "afmelden-herstart", At(end.AddDays(1), 10, 0));
        var link = OptOutLink(Assert.Single(Mails(app, "herstart@example.test")).TextBody);

        // Een nieuwe instantie van de app op dezelfde databank, zoals na een herstart of op een tweede server.
        await using var restarted = app.WithWebHostBuilder(_ => { });
        var client = restarted.CreateClient(new() { BaseAddress = new Uri("http://afmelden-herstart.test"), AllowAutoRedirect = false });
        var path = new Uri(link).PathAndQuery;
        var page = await WebsiteCart.PageAsync(client, path);
        var response = await WebsiteCart.PostAsync(client, path, page, new() { ["_handler"] = "afmelden" });
        Assert.Contains("Je krijgt van afmelden-herstart geen reviewvragen meer.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        await using var scope = app.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataProtectionKeys.AnyAsync());
    }

    /// <summary>De afmeldlink uit de tekstversie van de reviewvraag.</summary>
    private static string OptOutLink(string textBody) =>
        Regex.Match(textBody, @"Ik wil geen reviewvragen meer: (\S+)").Groups[1].Value is { Length: > 0 } link
            ? link
            : throw new InvalidOperationException("Geen afmeldlink in de mail.");

    /// <summary>Opent de afmeldlink en drukt op de knop; geeft de pagina daarna.</summary>
    private async Task<string> OptOutAsync(string host, string link)
    {
        var client = app.CreateClient(host);
        var path = new Uri(link).PathAndQuery;
        var page = await WebsiteCart.PageAsync(client, path);
        var response = await WebsiteCart.PostAsync(client, path, page, new() { ["_handler"] = "afmelden" });
        response.EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
