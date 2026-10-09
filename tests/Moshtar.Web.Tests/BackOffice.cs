using System.Net;
using System.Text.RegularExpressions;

namespace Moshtar.Web.Tests;

/// <summary>Hulpjes om het back office via HTTP te bedienen zoals een browser.</summary>
public static class BackOffice
{
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, bool rememberMe = false, string? returnUrl = null)
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

    public static string Field(string html, string name) =>
        Regex.Match(html, $"name=\"{Regex.Escape(name)}\" (?:type=\"hidden\" )?value=\"([^\"]+)\"").Groups[1].Value is { Length: > 0 } value
            ? WebUtility.HtmlDecode(value)
            : throw new InvalidOperationException($"Veld {name} niet gevonden.");
}
