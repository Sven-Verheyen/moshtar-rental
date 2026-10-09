using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Web.Identity;

namespace Moshtar.Web.Tests;

public sealed partial class PasswordRecoveryTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private const string NewPassword = "een gloednieuw wachtwoord";

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Login_page_links_to_password_recovery()
    {
        var page = await app.CreateClient(MoshtarApp.HopsakeeHost).GetStringAsync("/admin/inloggen");

        Assert.Contains("href=\"admin/wachtwoord-vergeten\"", page);
    }

    [Fact]
    public async Task User_gets_a_mail_with_a_link_to_choose_a_new_password_and_logs_in_with_it()
    {
        await app.CreateUserAsync("hopsakee", "vergeten@hopsakee.test", UserRole.Staff);
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        var request = await RequestAsync(client, "vergeten@hopsakee.test");

        Assert.Contains("Als dit e-mailadres bij ons gekend is", await request.Content.ReadAsStringAsync());
        var mail = Assert.Single(Outbox.Sent, m => m.To == "vergeten@hopsakee.test");
        Assert.Equal("noreply@hopsakee.test", mail.FromAddress);
        var reset = await ResetAsync(client, ResetLink(mail), NewPassword);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Contains("Je wachtwoord is gewijzigd", await client.GetStringAsync(reset.Headers.Location));
        var login = await BackOffice.LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "vergeten@hopsakee.test", NewPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task A_blocked_user_can_log_in_again_after_choosing_a_new_password()
    {
        await app.CreateUserAsync("hopsakee", "na-blokkering@hopsakee.test", UserRole.Staff);
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        for (var i = 0; i < 5; i++)
            await BackOffice.LoginAsync(client, "na-blokkering@hopsakee.test", "niet het juiste wachtwoord");
        await RequestAsync(client, "na-blokkering@hopsakee.test");

        await ResetAsync(client, ResetLink(Assert.Single(Outbox.Sent, m => m.To == "na-blokkering@hopsakee.test")), NewPassword);
        var login = await BackOffice.LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "na-blokkering@hopsakee.test", NewPassword);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task Unknown_email_address_gets_the_same_message_and_no_mail()
    {
        var known = await (await RequestAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "onbekend@hopsakee.test")).Content.ReadAsStringAsync();

        Assert.Contains("Als dit e-mailadres bij ons gekend is", known);
        Assert.DoesNotContain(Outbox.Sent, m => m.To == "onbekend@hopsakee.test");
    }

    [Fact]
    public async Task Request_on_the_domain_of_another_rental_company_finds_no_user()
    {
        await app.CreateUserAsync("hopsakee", "enkel-hopsakee@hopsakee.test");

        await RequestAsync(app.CreateClient(MoshtarApp.AndereHost), "enkel-hopsakee@hopsakee.test");

        Assert.DoesNotContain(Outbox.Sent, m => m.To == "enkel-hopsakee@hopsakee.test");
    }

    [Fact]
    public async Task Disabled_user_gets_no_mail()
    {
        var user = await app.CreateUserAsync("hopsakee", "uit@hopsakee.test", UserRole.Staff);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<UserAdministration>().DisableAsync(user.Id);

        await RequestAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "uit@hopsakee.test");

        Assert.DoesNotContain(Outbox.Sent, m => m.To == "uit@hopsakee.test");
    }

    [Fact]
    public async Task A_link_works_only_once()
    {
        await app.CreateUserAsync("hopsakee", "eenmalig@hopsakee.test", UserRole.Staff);
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        await RequestAsync(client, "eenmalig@hopsakee.test");
        var link = ResetLink(Assert.Single(Outbox.Sent, m => m.To == "eenmalig@hopsakee.test"));
        await ResetAsync(client, link, NewPassword);

        var again = await ResetAsync(app.CreateClient(MoshtarApp.HopsakeeHost), link, "nog een ander wachtwoord");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("verlopen of al gebruikt", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task New_password_must_be_at_least_12_characters()
    {
        await app.CreateUserAsync("hopsakee", "kort@hopsakee.test", UserRole.Staff);
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        await RequestAsync(client, "kort@hopsakee.test");

        var reset = await ResetAsync(client, ResetLink(Assert.Single(Outbox.Sent, m => m.To == "kort@hopsakee.test")), "te kort");

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Contains("minstens 12 tekens", await reset.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> RequestAsync(HttpClient client, string email)
    {
        var form = await client.GetStringAsync("/admin/wachtwoord-vergeten");
        return await client.PostAsync("/admin/wachtwoord-vergeten", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "wachtwoord-vergeten",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Input.Email"] = email,
        }));
    }

    private static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string link, string password)
    {
        var url = new Uri(link).PathAndQuery;
        var form = await client.GetStringAsync(url);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "wachtwoord-herstellen",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        }));
    }

    private static string ResetLink(OutgoingMail mail) =>
        LinkPattern().Match(mail.TextBody).Value is { Length: > 0 } link ? link : throw new InvalidOperationException("Geen link in de mail.");

    [GeneratedRegex(@"http://\S+/admin/wachtwoord-herstellen\S+")]
    private static partial Regex LinkPattern();
}
