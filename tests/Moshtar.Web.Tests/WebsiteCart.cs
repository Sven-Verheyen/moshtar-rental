using System.Net;
using System.Text.RegularExpressions;

namespace Moshtar.Web.Tests;

/// <summary>Een klant op de reservatiewebsite: in winkelkar leggen en reserveren in twee stappen, via de echte formulieren.</summary>
internal static class WebsiteCart
{
    /// <summary>Klikt "In winkelkar" op de pagina van een artikel of pakket, met de huurperiode uit de URL.</summary>
    public static async Task<HttpResponseMessage> AddAsync(HttpClient client, string path, int quantity = 1)
    {
        var page = await PageAsync(client, path);
        return await PostAsync(client, "/winkelkar/toevoegen", page, new()
        {
            ["soort"] = BackOffice.Field(page, "soort"),
            ["id"] = BackOffice.Field(page, "id"),
            ["van"] = BackOffice.Field(page, "van"),
            ["tot"] = BackOffice.Field(page, "tot"),
            ["terug"] = BackOffice.Field(page, "terug"),
            ["aantal"] = quantity.ToString(),
        });
    }

    /// <summary>Stap 1 op de winkelkarpagina: het e-mailadres en de vinkjes.</summary>
    public static async Task<HttpResponseMessage> IdentifyAsync(HttpClient client, string email, bool reminder = false, bool news = false, string cartPath = "/winkelkar")
    {
        var page = await PageAsync(client, cartPath);
        var fields = new Dictionary<string, string>
        {
            ["email"] = email,
            ["terug"] = BackOffice.Field(page, "terug"),
            ["taal"] = BackOffice.Field(page, "taal"),
        };
        if (reminder) fields["herinnering"] = "ja";
        if (news) fields["nieuws"] = "ja";
        return await PostAsync(client, "/winkelkar/gegevens", page, fields);
    }

    /// <summary>Stap 2 op de winkelkarpagina: de rest van de gegevens en "Reserveren".</summary>
    public static async Task<HttpResponseMessage> ReserveAsync(HttpClient client, string firstName, string cartPath = "/winkelkar",
        string delivery = "Delivery", string? phone = "0470 12 34 56", string street = "Kerkstraat 1")
    {
        var page = await PageAsync(client, cartPath);
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "reservation",
            ["Form.FirstName"] = firstName,
            ["Form.LastName"] = "Klant",
            ["Form.Street"] = street,
            ["Form.PostalCode"] = "2000",
            ["Form.City"] = "Antwerpen",
            ["Form.DeliveryMethod"] = delivery,
        };
        if (phone is not null) fields["Form.Phone"] = phone;
        return await PostAsync(client, cartPath, page, fields);
    }

    /// <summary>Alles in één keer: in winkelkar, stap 1 en stap 2. Geeft de pagina na het reserveren.</summary>
    public static async Task<string> ReserveItemAsync(HttpClient client, string itemPath, string email, string firstName,
        string cartPath = "/winkelkar", string delivery = "Delivery", string? phone = "0470 12 34 56", string street = "Kerkstraat 1")
    {
        await AddAsync(client, itemPath);
        await IdentifyAsync(client, email, cartPath: cartPath);
        var response = await ReserveAsync(client, firstName, cartPath, delivery, phone, street);
        response.EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = BackOffice.Field(page, "__RequestVerificationToken");
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static async Task<string> PageAsync(HttpClient client, string path) => WebUtility.HtmlDecode(await client.GetStringAsync(path));

    public static int Count(string html, string text) => Regex.Matches(html, Regex.Escape(text)).Count;
}
