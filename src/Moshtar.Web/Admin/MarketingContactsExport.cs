using System.Text;
using Microsoft.AspNetCore.Authorization;
using Moshtar.Application.Marketing;
using Moshtar.Domain.Marketing;
using Moshtar.Web.Identity;

namespace Moshtar.Web.Admin;

/// <summary>
/// De marketingcontacten als CSV, voor de beheerder: puntkomma's en UTF-8 met BOM, zodat Excel in België het bestand
/// meteen in kolommen en met de juiste tekens opent.
/// </summary>
public static class MarketingContactsExport
{
    public const string Path = "/admin/marketingcontacten.csv";

    private static readonly TimeZoneInfo BelgianTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static IEndpointRouteBuilder MapMarketingContactsExport(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, async (IMarketingContactAdministration contacts, HttpContext context) =>
            Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(await contacts.ListAsync(context.RequestAborted)))).ToArray(),
                "text/csv; charset=utf-8", "marketingcontacten.csv"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Administrator });
        return app;
    }

    public static string Csv(IEnumerable<MarketingContactView> contacts)
    {
        var csv = new StringBuilder("E-mailadres;Taal;Toestemming;Herkomst\r\n");
        foreach (var c in contacts)
            csv.Append(Field(c.Email)).Append(';')
                .Append(Field(c.Culture)).Append(';')
                .Append(Local(c.ConsentedAtUtc)).Append(';')
                .Append(Field(SourceName(c.Source))).Append("\r\n");
        return csv.ToString();
    }

    public static string SourceName(MarketingContactSource source) => source switch
    {
        MarketingContactSource.Reservation => "Reservatie",
        _ => "Winkelkar",
    };

    public static string Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BelgianTime).ToString("dd/MM/yyyy HH:mm");

    /// <summary>
    /// Een veld zoals Excel het veilig leest: tussen aanhalingstekens als het een scheidingsteken bevat, en met een
    /// apostrof ervoor als het met =, +, - of @ begint, zodat Excel het nooit als formule uitvoert.
    /// </summary>
    private static string Field(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
