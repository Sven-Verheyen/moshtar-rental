using System.Net;
using System.Text.RegularExpressions;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Web.Tests;

public sealed partial class BackOfficeLoginTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task Anonymous_visitor_of_the_back_office_is_sent_to_the_login_page()
    {
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        var response = await client.GetAsync("/admin/artikelen");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/inloggen", response.Headers.Location!.AbsolutePath);
        Assert.Contains("ReturnUrl=%2Fadmin%2Fartikelen", response.Headers.Location.Query);
    }

    [Fact]
    public async Task User_logs_in_with_email_and_password_and_reaches_the_back_office()
    {
        await app.CreateUserAsync("hopsakee", "sven@hopsakee.test");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        var login = await LoginAsync(client, "sven@hopsakee.test", MoshtarApp.Password, returnUrl: "/admin/artikelen");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/admin/artikelen", login.Headers.Location!.AbsolutePath);
        var page = await client.GetAsync("/admin/artikelen");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("sven@hopsakee.test", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_password_shows_a_message_and_gives_no_access()
    {
        await app.CreateUserAsync("hopsakee", "fout@hopsakee.test");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        var login = await LoginAsync(client, "fout@hopsakee.test", "niet het juiste wachtwoord");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("E-mailadres of wachtwoord klopt niet.", await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_block_the_user_even_with_the_right_password()
    {
        await app.CreateUserAsync("hopsakee", "geblokkeerd@hopsakee.test");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        for (var i = 0; i < 5; i++)
            await LoginAsync(client, "geblokkeerd@hopsakee.test", "niet het juiste wachtwoord");
        var login = await LoginAsync(client, "geblokkeerd@hopsakee.test", MoshtarApp.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("tijdelijk geblokkeerd", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task User_of_one_rental_company_cannot_log_in_on_the_domain_of_another()
    {
        await app.CreateUserAsync("hopsakee", "alleen-hopsakee@hopsakee.test");
        var client = app.CreateClient(MoshtarApp.AndereHost);

        var login = await LoginAsync(client, "alleen-hopsakee@hopsakee.test", MoshtarApp.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("E-mailadres of wachtwoord klopt niet.", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Same_email_address_is_a_separate_user_at_each_rental_company()
    {
        await app.CreateUserAsync("hopsakee", "dubbel@example.test", password: "wachtwoord bij hopsakee");
        await app.CreateUserAsync("andere", "dubbel@example.test", UserRole.Staff, password: "wachtwoord bij andere");
        var hopsakee = app.CreateClient(MoshtarApp.HopsakeeHost);
        var andere = app.CreateClient(MoshtarApp.AndereHost);

        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(hopsakee, "dubbel@example.test", "wachtwoord bij hopsakee")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(andere, "dubbel@example.test", "wachtwoord bij hopsakee")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(andere, "dubbel@example.test", "wachtwoord bij andere")).StatusCode);
    }

    [Fact]
    public async Task Login_cookie_of_one_rental_company_is_refused_by_another()
    {
        await app.CreateUserAsync("hopsakee", "cookie@hopsakee.test");
        var hopsakee = app.CreateClient(MoshtarApp.HopsakeeHost);
        var login = await LoginAsync(hopsakee, "cookie@hopsakee.test", MoshtarApp.Password);
        var cookie = AuthCookie(login)!.Split(';')[0];

        var andere = app.CreateClient(MoshtarApp.AndereHost);
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin");
        request.Headers.Add("Cookie", cookie);
        var response = await andere.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/inloggen", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Remember_me_keeps_the_user_logged_in_for_14_days()
    {
        await app.CreateUserAsync("hopsakee", "onthoud@hopsakee.test");

        var remembered = AuthCookie(await LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "onthoud@hopsakee.test", MoshtarApp.Password, rememberMe: true));
        var session = AuthCookie(await LoginAsync(app.CreateClient(MoshtarApp.HopsakeeHost), "onthoud@hopsakee.test", MoshtarApp.Password));

        var expires = DateTimeOffset.Parse(ExpiresPattern().Match(remembered!).Groups[1].Value);
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(13.9), TimeSpan.FromDays(14.1));
        Assert.DoesNotContain("expires=", session!);
    }

    [Fact]
    public async Task Logging_out_ends_access_to_the_back_office()
    {
        await app.CreateUserAsync("hopsakee", "uitloggen@hopsakee.test");
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);
        await LoginAsync(client, "uitloggen@hopsakee.test", MoshtarApp.Password);
        var page = await (await client.GetAsync("/admin/artikelen")).Content.ReadAsStringAsync();

        var logout = await client.PostAsync("/admin/uitloggen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(page, "__RequestVerificationToken"),
        }));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task There_is_no_page_to_register_yourself()
    {
        var client = app.CreateClient(MoshtarApp.HopsakeeHost);

        var page = await (await client.GetAsync("/admin/inloggen")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("registr", page, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, bool rememberMe = false, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/admin/inloggen" : $"/admin/inloggen?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        var form = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = Field(form, "__RequestVerificationToken"),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        };
        if (rememberMe) fields["Input.RememberMe"] = "true";
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static string Field(string html, string name) =>
        Regex.Match(html, $"name=\"{Regex.Escape(name)}\" (?:type=\"hidden\" )?value=\"([^\"]+)\"").Groups[1].Value is { Length: > 0 } value
            ? WebUtility.HtmlDecode(value)
            : throw new InvalidOperationException($"Veld {name} niet gevonden.");

    private static string? AuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(".Moshtar.BackOffice="))
            : null;

    [GeneratedRegex("expires=([^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiresPattern();
}
