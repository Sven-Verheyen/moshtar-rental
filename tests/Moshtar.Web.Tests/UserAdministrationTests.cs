using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Web.Identity;

namespace Moshtar.Web.Tests;

public sealed partial class UserAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Uri Hopsakee = new($"http://{MoshtarApp.HopsakeeHost}/");

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Administrator_sees_only_the_users_of_their_own_rental_company()
    {
        await app.CreateUserAsync("hopsakee", "eigen@hopsakee.test");
        await app.CreateUserAsync("andere", "vreemd@andere.test");

        var users = await WithUsersAsync(u => u.ListAsync());

        Assert.Contains(users, u => u.Email == "eigen@hopsakee.test" && u.Role == UserRole.Administrator && u.IsActive);
        Assert.DoesNotContain(users, u => u.Email == "vreemd@andere.test");
    }

    [Fact]
    public async Task Invited_user_gets_a_mail_with_a_link_and_chooses_a_password_to_log_in()
    {
        await WithUsersAsync(u => u.InviteAsync("nieuw@hopsakee.test", UserRole.Staff, Hopsakee));

        var mail = Assert.Single(Outbox.Sent, m => m.To == "nieuw@hopsakee.test");
        Assert.Equal("noreply@hopsakee.test", mail.FromAddress);
        Assert.Contains("48 uur", mail.TextBody);
        var invited = Assert.Single(await WithUsersAsync(u => u.ListAsync()), u => u.Email == "nieuw@hopsakee.test");
        Assert.False(invited.HasAcceptedInvite);

        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        var accepted = await AcceptAsync(client, InviteLink(mail), "mijn eigen lange wachtwoord");

        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/reservaties")).StatusCode);
        Assert.True(Assert.Single(await WithUsersAsync(u => u.ListAsync()), u => u.Email == "nieuw@hopsakee.test").HasAcceptedInvite);
    }

    [Fact]
    public async Task Invite_link_is_valid_for_48_hours()
    {
        var options = app.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;

        Assert.Equal(TimeSpan.FromHours(48), options.TokenLifespan);
    }

    [Fact]
    public async Task Resending_an_invite_replaces_the_old_link()
    {
        await WithUsersAsync(u => u.InviteAsync("opnieuw@hopsakee.test", UserRole.Staff, Hopsakee));
        var oldLink = InviteLink(Assert.Single(Outbox.Sent, m => m.To == "opnieuw@hopsakee.test"));
        var id = (await WithUsersAsync(u => u.ListAsync())).Single(u => u.Email == "opnieuw@hopsakee.test").Id;

        await WithUsersAsync(u => u.ResendInviteAsync(id, Hopsakee));
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        var withOldLink = await AcceptAsync(client, oldLink, "mijn eigen lange wachtwoord");

        Assert.Equal(HttpStatusCode.OK, withOldLink.StatusCode);
        Assert.Contains("verlopen of al gebruikt", await withOldLink.Content.ReadAsStringAsync());
        Assert.Equal(2, Outbox.Sent.Count(m => m.To == "opnieuw@hopsakee.test"));
    }

    [Fact]
    public async Task An_email_address_can_be_invited_only_once_per_rental_company()
    {
        await app.CreateUserAsync("hopsakee", "bestaat@hopsakee.test");

        var error = await Assert.ThrowsAsync<UserAdministrationException>(() =>
            WithUsersAsync(u => u.InviteAsync("bestaat@hopsakee.test", UserRole.Staff, Hopsakee)));

        Assert.Contains("bestaat al", error.Message);
    }

    [Fact]
    public async Task Disabled_user_cannot_log_in_and_is_logged_out_of_open_sessions()
    {
        var client = await app.LoggedInClientAsync("uitschakelen@hopsakee.test", UserRole.Staff);
        var id = await IdOfAsync("uitschakelen@hopsakee.test");

        await WithUsersAsync(u => u.DisableAsync(id));

        var openSession = await client.GetAsync("/admin/reservaties");
        Assert.Equal(HttpStatusCode.Redirect, openSession.StatusCode);
        Assert.Equal("/admin/inloggen", openSession.Headers.Location!.AbsolutePath);
        var login = await BackOffice.LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "uitschakelen@hopsakee.test", MoshtarApp.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(Assert.Single(await WithUsersAsync(u => u.ListAsync()), u => u.Id == id).IsActive);
    }

    [Fact]
    public async Task Disabled_user_can_be_enabled_again()
    {
        await app.CreateUserAsync("hopsakee", "terug@hopsakee.test", UserRole.Staff);
        var id = await IdOfAsync("terug@hopsakee.test");
        await WithUsersAsync(u => u.DisableAsync(id));

        await WithUsersAsync(u => u.EnableAsync(id));

        var login = await BackOffice.LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "terug@hopsakee.test", MoshtarApp.Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task Changing_the_role_changes_what_the_user_may_do()
    {
        await app.CreateUserAsync("hopsakee", "promotie@hopsakee.test", UserRole.Staff);
        var id = await IdOfAsync("promotie@hopsakee.test");

        await WithUsersAsync(u => u.ChangeRoleAsync(id, UserRole.Administrator));

        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        await BackOffice.LoginAsync(client, "promotie@hopsakee.test", MoshtarApp.Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/gebruikers")).StatusCode);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_disabled_or_made_staff()
    {
        // Een eigen verhuurder, zodat Beheerders uit andere tests niet meetellen.
        await app.CreateTenantAsync("een-beheerder", "een-beheerder.test");
        var admin = await app.CreateUserAsync("een-beheerder", "enige@een-beheerder.test");
        var other = await app.CreateUserAsync("een-beheerder", "tweede@een-beheerder.test");
        await WithUsersAsync(u => u.DisableAsync(other.Id), "een-beheerder");

        var disable = await Assert.ThrowsAsync<UserAdministrationException>(() => WithUsersAsync(u => u.DisableAsync(admin.Id), "een-beheerder"));
        var demote = await Assert.ThrowsAsync<UserAdministrationException>(() => WithUsersAsync(u => u.ChangeRoleAsync(admin.Id, UserRole.Staff), "een-beheerder"));

        Assert.Contains("minstens één actieve Beheerder", disable.Message);
        Assert.Contains("minstens één actieve Beheerder", demote.Message);
    }

    [Fact]
    public async Task Administrator_cannot_touch_a_user_of_another_rental_company()
    {
        var stranger = await app.CreateUserAsync("andere", "buitenstaander@andere.test", UserRole.Staff);

        await Assert.ThrowsAsync<UserAdministrationException>(() => WithUsersAsync(u => u.DisableAsync(stranger.Id)));
    }

    [Fact]
    public async Task Staff_has_no_access_to_user_management()
    {
        var client = await app.LoggedInClientAsync("geen-beheer@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/gebruikers");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/geen-toegang", response.Headers.Location!.AbsolutePath);
    }

    private async Task<T> WithUsersAsync<T>(Func<UserAdministration, Task<T>> action, string tenantSlug = "hopsakee")
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        return await action(scope.ServiceProvider.GetRequiredService<UserAdministration>());
    }

    private Task<bool> WithUsersAsync(Func<UserAdministration, Task> action, string tenantSlug = "hopsakee") =>
        WithUsersAsync(async u => { await action(u); return true; }, tenantSlug);

    private async Task<Guid> IdOfAsync(string email) =>
        (await WithUsersAsync(u => u.ListAsync())).Single(u => u.Email == email).Id;

    private static string InviteLink(OutgoingMail mail) =>
        LinkPattern().Match(mail.TextBody).Value is { Length: > 0 } link ? link : throw new InvalidOperationException("Geen link in de mail.");

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient client, string link, string password)
    {
        var url = new Uri(link).PathAndQuery;
        var form = await client.GetStringAsync(url);
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "uitnodiging",
            ["__RequestVerificationToken"] = BackOffice.Field(form, "__RequestVerificationToken"),
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        }));
    }

    [GeneratedRegex(@"http://\S+/admin/uitnodiging\S+")]
    private static partial Regex LinkPattern();
}
