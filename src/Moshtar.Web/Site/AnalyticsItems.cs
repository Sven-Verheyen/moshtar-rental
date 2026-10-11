using System.Text.Json.Nodes;

namespace Moshtar.Web.Site;

/// <summary>
/// De GA4-ecommerce-velden voor de winkelkar en de reservatie: waarde, munt en per lijn het artikel, het aantal en de
/// prijs per stuk voor de huurperiode. Nooit persoonsgegevens.
/// </summary>
public static class AnalyticsItems
{
    public static JsonObject Event(decimal value, IEnumerable<JsonObject> items) => new()
    {
        ["value"] = value,
        ["currency"] = "EUR",
        ["items"] = new JsonArray(items.ToArray<JsonNode>()),
    };

    public static JsonObject Item(string id, string name, int quantity, decimal price) => new()
    {
        ["item_id"] = id,
        ["item_name"] = name,
        ["quantity"] = quantity,
        ["price"] = price,
    };
}
