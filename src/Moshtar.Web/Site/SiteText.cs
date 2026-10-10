using System.Globalization;

namespace Moshtar.Web.Site;

/// <summary>Kleine teksthulpjes voor de reservatiewebsite.</summary>
public static class SiteText
{
    /// <summary>De eerste letter van een naam, voor een artikel zonder foto; ook juist bij een emoji of een lege naam.</summary>
    public static string Initial(string name) =>
        name.Length == 0 ? "" : name[..StringInfo.GetNextTextElementLength(name)].ToUpperInvariant();
}
