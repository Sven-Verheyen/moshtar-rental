using System.Globalization;

namespace Moshtar.Web.Site;

/// <summary>Kleine teksthulpjes voor de reservatiewebsite.</summary>
public static class SiteText
{
    /// <summary>De eerste letter van een naam, voor een artikel zonder foto; ook juist bij een emoji of een lege naam.</summary>
    public static string Initial(string name) =>
        name.Length == 0 ? "" : name[..StringInfo.GetNextTextElementLength(name)].ToUpperInvariant();

    /// <summary>De alinea's van een tekst die een verhuurder invulde: gescheiden door een lege regel.</summary>
    public static IEnumerable<string> Paragraphs(string text) =>
        text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Een huurperiode om te lezen, in de taal van de pagina: "12 oktober 2026" of "12 oktober – 14 oktober 2026".</summary>
    public static string Period(Moshtar.Domain.Common.DateRange period)
    {
        var culture = CultureInfo.CurrentUICulture;
        var end = period.End.ToString("d MMMM yyyy", culture);
        if (period.Days == 1) return end;
        var start = period.Start.ToString(period.Start.Year == period.End.Year ? "d MMMM" : "d MMMM yyyy", culture);
        return $"{start} – {end}";
    }

    /// <summary>De prijs van een bedrag in euro, zoals overal op de reservatiewebsite: "€ 150,00".</summary>
    public static string Money(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("nl-BE"));

    /// <summary>Een tekst als meta-beschrijving: op één regel en hoogstens 160 tekens, anders ingekort op een woord.</summary>
    public static string Summary(string? text, int maxLength = 160)
    {
        var line = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= maxLength) return line;
        var cut = line[..(maxLength - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > maxLength / 2 ? cut[..space] : cut).TrimEnd(',', '.', ';', ':') + "…";
    }
}
