using System.Globalization;
using System.Text.RegularExpressions;

namespace Moshtar.Web.Site;

/// <summary>Kleine teksthulpjes voor de reservatiewebsite.</summary>
public static partial class SiteText
{
    /// <summary>De eerste letter van een naam, voor een artikel zonder foto; ook juist bij een emoji of een lege naam.</summary>
    public static string Initial(string name) =>
        name.Length == 0 ? "" : name[..StringInfo.GetNextTextElementLength(name)].ToUpperInvariant();

    /// <summary>Een telefoonnummer zoals het in een tel:-link moet: alleen + en cijfers, bv. "0475/12.34.56" wordt "0475123456".</summary>
    public static string PhoneLink(string phone) => NotDialable().Replace(phone, "");

    [GeneratedRegex("[^+0-9]")]
    private static partial Regex NotDialable();
}
