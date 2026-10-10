using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Moshtar.Domain.Mail;

/// <summary>
/// De beperkte opmaak van een mailsjabloon (ADR 0003): **vet**, *cursief* en [tekst](adres) voor links,
/// binnen een alinea. Andere tekens blijven gewone tekst; HTML bestaat niet.
/// </summary>
public static partial class MailFormatting
{
    private static readonly string[] AllowedSchemes = ["https://", "http://", "mailto:"];

    /// <summary>
    /// Zet één alinea om naar een veilige HTML-versie en een tekstversie. De opmaak wordt gelezen vóór de
    /// plaatshouders ingevuld worden, zodat tekens in wat een klant intypte nooit opmaak worden.
    /// </summary>
    public static (string Html, string Text) Render(string paragraph, Func<string, string?> value)
    {
        var html = new StringBuilder();
        var text = new StringBuilder();
        var position = 0;
        foreach (Match m in Markup().Matches(paragraph))
        {
            Plain(paragraph[position..m.Index]);
            position = m.Index + m.Length;
            if (m.Groups["bold"].Success)
            {
                var inner = Fill(m.Groups["bold"].Value);
                html.Append("<strong>").Append(E(inner)).Append("</strong>");
                text.Append(inner);
            }
            else if (m.Groups["italic"].Success)
            {
                var inner = Fill(m.Groups["italic"].Value);
                html.Append("<em>").Append(E(inner)).Append("</em>");
                text.Append(inner);
            }
            else
            {
                var label = Fill(m.Groups["label"].Value);
                var url = Fill(m.Groups["url"].Value);
                if (!IsAllowed(m.Groups["url"].Value) || !IsAllowed(url))
                {
                    Plain(m.Value);
                    continue;
                }
                html.Append("<a href=\"").Append(E(url)).Append("\">").Append(E(label)).Append("</a>");
                text.Append(label == url ? url : $"{label} ({url})");
            }
        }
        Plain(paragraph[position..]);
        return (html.ToString().ReplaceLineEndings("<br>"), text.ToString());

        void Plain(string part)
        {
            var filled = Fill(part);
            html.Append(E(filled));
            text.Append(filled);
        }

        string Fill(string part) => MailKindDefinition.Fill(part, value);
    }

    /// <summary>De adressen van links die niet met een toegelaten schema beginnen.</summary>
    public static IEnumerable<string> ForbiddenLinks(string body) =>
        Markup().Matches(body).Where(m => m.Groups["url"].Success && !IsAllowed(m.Groups["url"].Value)).Select(m => m.Groups["url"].Value);

    private static bool IsAllowed(string url) =>
        AllowedSchemes.Any(s => url.StartsWith(s, StringComparison.OrdinalIgnoreCase) && url.Length > s.Length);

    private static string E(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\*\*(?<bold>[^*\n]+)\*\*|\*(?<italic>[^*\n]+)\*|\[(?<label>[^\]\n]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex Markup();
}
