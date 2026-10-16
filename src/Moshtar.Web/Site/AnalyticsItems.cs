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

    /// <summary>
    /// Een lijn voor <see cref="NetEvent"/>: <see cref="LineTotal"/> is alle stuks samen voor de kortingen,
    /// <see cref="Discount"/> wat de kortingen van deze lijn samen aftrekken (positief).
    /// </summary>
    public sealed record Line(string Id, string Name, int Quantity, decimal LineTotal, decimal Discount);

    /// <summary>
    /// Elke lijn aan haar prijs per stuk na de kortingen ((lijntotaal − korting) / aantal, op de cent), zonder
    /// kortingslijnen als items, en als waarde de som van prijs × aantal, zoals GA4 ze zelf optelt. Deelt een korting
    /// niet mooi over de stuks van een lijn, dan kan die waarde enkele centen van het totaal van de winkelkar afwijken.
    /// </summary>
    public static JsonObject NetEvent(IEnumerable<Line> lines)
    {
        var priced = lines.Select(l => (Line: l, Price: Cents((l.LineTotal - l.Discount) / l.Quantity))).ToList();
        return Event(priced.Sum(p => p.Price * p.Line.Quantity), priced.Select(p => Item(p.Line.Id, p.Line.Name, p.Line.Quantity, p.Price)));
    }

    /// <summary>
    /// Verdeelt een korting over lijnen naar hun bedrag, op de cent; de laatste lijn krijgt de rest, zodat de som klopt.
    /// Voor een bewaarde reservatie, die alleen haar kortingslijnen kent en niet welk stuk welke korting kreeg: een
    /// benadering, alleen voor GA4.
    /// </summary>
    public static IReadOnlyList<decimal> Spread(decimal discount, IReadOnlyList<decimal> lineTotals)
    {
        var sum = lineTotals.Sum();
        if (sum == 0 || lineTotals.Count == 0) return lineTotals.Select(_ => 0m).ToList();
        var shares = lineTotals.Select(t => Cents(discount * t / sum)).ToList();
        shares[^1] = discount - shares.Take(shares.Count - 1).Sum();
        return shares;
    }

    private static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
