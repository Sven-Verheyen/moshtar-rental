using System.Text;
using System.Text.RegularExpressions;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Mail;

/// <summary>Een vast blok in een mail, zoals het overzicht van een reservatie, in HTML en als tekst.</summary>
internal sealed record MailBlock(string Html, string Text);

/// <summary>
/// De HTML- en tekstversie van een mail uit de tekst van een mailsjabloon (ADR 0003). Elke alinea wordt een
/// &lt;p&gt;; een blokplaatshouder wordt zijn vaste blok, ook midden in een alinea.
/// </summary>
internal static class MailBody
{
    public static (string Html, string Text) Compose(
        Tenant tenant, string? logoUrl, string body, Func<string, string?> value, IReadOnlyDictionary<string, MailBlock> blocks)
    {
        var text = new List<string>();
        var html = new StringBuilder(MailHeader.Html(tenant, logoUrl));
        var pattern = new Regex(@"\{(" + string.Join('|', blocks.Keys.Select(Regex.Escape)) + @")\}");
        foreach (var paragraph in Paragraphs(body))
        {
            var position = 0;
            foreach (Match m in pattern.Matches(paragraph))
            {
                Part(paragraph[position..m.Index]);
                var block = blocks[m.Groups[1].Value];
                // Een blok zonder tekstversie, zoals een QR-code, staat enkel in de HTML.
                if (block.Text.Length > 0) text.Add(block.Text);
                html.Append(block.Html);
                position = m.Index + m.Length;
            }
            Part(paragraph[position..]);
        }
        return (html.ToString(), string.Join(Environment.NewLine + Environment.NewLine, text));

        void Part(string part)
        {
            var (partHtml, partText) = MailFormatting.Render(part.Trim(), value);
            if (partText.Trim().Length == 0) return;
            text.Add(partText.Trim());
            html.Append($"<p>{partHtml.Trim()}</p>");
        }
    }

    private static IEnumerable<string> Paragraphs(string body) =>
        body.ReplaceLineEndings("\n").Split("\n\n").Select(p => p.Trim()).Where(p => p.Length > 0);
}
