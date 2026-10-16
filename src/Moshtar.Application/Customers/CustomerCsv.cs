using System.Text;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;

namespace Moshtar.Application.Customers;

/// <summary>
/// Het CSV-formaat van het klantenbestand, voor export, import en voorbeeldbestand: puntkomma's en UTF-8 met BOM, zoals
/// de marketingcontacten, zodat Excel in België het bestand meteen in kolommen en met de juiste tekens opent.
/// </summary>
public static class CustomerCsv
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "Type", "Voornaam", "Achternaam", "Bedrijfsnaam", "Btw-nummer", "E-mail", "Telefoon", "Straat", "Postcode", "Gemeente", "Land",
        "Factuurstraat", "Factuurpostcode", "Factuurgemeente", "Factuurland", "Taal", "Notitie",
    ];

    public const string Private = "Particulier";
    public const string Business = "Zakelijk";

    /// <summary>De klanten als CSV-tekst, zonder BOM.</summary>
    public static string Write(IEnumerable<Customer> customers)
    {
        var csv = new StringBuilder();
        Line(csv, Columns);
        foreach (var c in customers)
            Line(csv,
            [
                c.Kind == CustomerKind.Business ? Business : Private, c.FirstName, c.LastName, c.CompanyName, c.VatNumber, c.Email, c.Phone,
                c.Address.Street, c.Address.PostalCode, c.Address.City, c.Address.Country,
                c.BillingAddress?.Street, c.BillingAddress?.PostalCode, c.BillingAddress?.City, c.BillingAddress?.Country,
                c.PreferredCulture, c.Notes,
            ]);
        return csv.ToString();
    }

    /// <summary>Het voorbeeldbestand om te downloaden: de kolommen met een particuliere en een zakelijke klant.</summary>
    public static string Sample()
    {
        var csv = new StringBuilder();
        Line(csv, Columns);
        Line(csv, [Private, "Els", "Peeters", "", "", "els.peeters@example.com", "+32470123456", "Markt 1", "9000", "Gent", "BE", "", "", "", "", "nl", ""]);
        Line(csv,
        [
            Business, "Jan", "Smet", "Basisschool De Linde", "BE0123456789", "secretariaat@delinde.example.com", "+3291234567",
            "Schoolstraat 3", "9050", "Gentbrugge", "BE", "Postbus 12", "9050", "Gentbrugge", "BE", "nl", "Factuur via het secretariaat",
        ]);
        return csv.ToString();
    }

    /// <summary>Het verslag van een import: elke rij die niet geïmporteerd werd, met regelnummer, naam, e-mail en reden.</summary>
    public static string SkippedReport(IEnumerable<SkippedCustomer> skipped)
    {
        var csv = new StringBuilder();
        Line(csv, ["Regel", "Naam", "E-mail", "Reden"]);
        foreach (var s in skipped)
            Line(csv, [s.Line.ToString(), s.Name, s.Email, s.Reason]);
        return csv.ToString();
    }

    /// <summary>
    /// Leest een CSV-bestand met de kolommen van <see cref="Columns"/> (in eender welke volgorde, hoofdletters maken
    /// niet uit) en een puntkomma of komma als scheidingsteken, zoals de kolomnamen het gebruiken. Velden tussen
    /// aanhalingstekens mogen scheidingstekens en regeleinden bevatten; een BOM vooraan en lege regels tellen niet.
    /// </summary>
    public static CsvFile Read(string text)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.Contains(';') || !firstLine.Contains(',') ? ';' : ',';
        var records = Records(text, separator).ToList();
        if (records.Count == 0) return new CsvFile([], "Het bestand is leeg.");

        var header = records[0].Fields.Select(Normalize).ToList();
        var missing = Columns.Where(c => !header.Contains(Normalize(c))).ToList();
        if (missing.Count > 0)
            return new CsvFile([], $"Deze kolommen ontbreken: {string.Join(", ", missing)}. Download het voorbeeldbestand voor de juiste kolommen.");

        var rows = records.Skip(1)
            .Where(r => r.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            .Select(r => new CsvRow(r.Line, Columns.ToDictionary(c => c, c =>
            {
                var index = header.IndexOf(Normalize(c));
                return index < r.Fields.Count ? Unescape(r.Fields[index].Trim()) : "";
            })))
            .ToList();
        return new CsvFile(rows, null);
    }

    /// <summary>
    /// De klantgegevens van een rij, met wat er niet aan klopt bovenop de regels van <see cref="CustomerDetails.Errors"/>:
    /// het type, de taal (een van <paramref name="cultures"/>; leeg is <paramref name="defaultCulture"/>) en de
    /// landcodes. Zonder type is een rij met een bedrijfsnaam zakelijk; zonder land is het adres in België.
    /// </summary>
    public static (CustomerDetails Details, IReadOnlyList<string> Errors) Details(CsvRow row, IReadOnlyList<string> cultures, string defaultCulture)
    {
        string? Optional(string column) => string.IsNullOrWhiteSpace(row[column]) ? null : row[column];
        var errors = new List<string>();
        var kind = row["Type"].ToLowerInvariant() switch
        {
            "particulier" => CustomerKind.Private,
            "zakelijk" => CustomerKind.Business,
            "" => Optional("Bedrijfsnaam") is null ? CustomerKind.Private : CustomerKind.Business,
            _ => (CustomerKind?)null,
        };
        if (kind is null) errors.Add($"Het type moet {Private} of {Business} zijn.");

        var billing = new[] { "Factuurstraat", "Factuurpostcode", "Factuurgemeente" }.Any(c => Optional(c) is not null)
            ? new Address { Street = row["Factuurstraat"], PostalCode = row["Factuurpostcode"], City = row["Factuurgemeente"], Country = row["Factuurland"] }
            : null;
        var culture = Optional("Taal")?.ToLowerInvariant() ?? defaultCulture;
        var details = new CustomerDetails(kind ?? CustomerKind.Private, row["Voornaam"], row["Achternaam"], row["E-mail"], Optional("Telefoon"),
            new Address { Street = row["Straat"], PostalCode = row["Postcode"], City = row["Gemeente"], Country = row["Land"] }, culture,
            Optional("Bedrijfsnaam"), Optional("Btw-nummer"), billing, Optional("Notitie"));

        errors.AddRange(details.Errors());
        if (!cultures.Contains(culture)) errors.Add($"De taal moet een van deze zijn: {string.Join(", ", cultures)}.");
        if (!IsCountry(row["Land"]) || (billing is not null && !IsCountry(row["Factuurland"])))
            errors.Add("Het land moet een landcode van twee letters zijn, bv. BE.");
        return (details, errors);
    }

    /// <summary>Tekst als CSV-bestand: UTF-8 met BOM.</summary>
    public static byte[] Bytes(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    /// <summary>Een regel met deze velden, elk zoals <see cref="Field"/> het schrijft.</summary>
    public static void Line(StringBuilder csv, IEnumerable<string?> fields) =>
        csv.AppendJoin(';', fields.Select(f => Field(f ?? ""))).Append("\r\n");

    /// <summary>
    /// Een veld zoals Excel het veilig leest: tussen aanhalingstekens als het een scheidingsteken bevat, en met een
    /// apostrof ervoor als het met =, +, -, @, een tab of een carriage return begint (de lijst van OWASP), zodat Excel het
    /// nooit als formule uitvoert. De import haalt die apostrof weer weg.
    /// </summary>
    public static string Field(string value)
    {
        if (value.Length > 0 && FormulaStart.Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([';', ',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>Waarmee een veld begint dat Excel als formule zou kunnen lezen.</summary>
    private const string FormulaStart = "=+-@\t\r";

    /// <summary>Haalt de apostrof weg die <see cref="Field"/> voor een veld vooraan zet dat als formule gelezen kan worden.</summary>
    private static string Unescape(string value) =>
        value.Length > 1 && value[0] == '\'' && FormulaStart.Contains(value[1]) ? value[1..] : value;

    private static string Normalize(string column) => column.Trim().ToLowerInvariant() switch
    {
        "e-mailadres" or "email" or "emailadres" => "e-mail",
        "btw-nummer" or "btwnummer" or "btw" => "btw-nummer",
        var name => name,
    };

    private static bool IsCountry(string country) =>
        country.Length == 0 || (country.Trim().Length == 2 && country.Trim().All(char.IsAsciiLetter));

    /// <summary>De records van de tekst, elk met het regelnummer waarop het begint.</summary>
    private static IEnumerable<(int Line, List<string> Fields)> Records(string text, char separator)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var start = 1;
        var quoted = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else
                {
                    if (ch == '\n') line++;
                    field.Append(ch);
                }
                continue;
            }
            any = true;
            if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == separator) { fields.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                field.Clear();
                yield return (start, fields);
                fields = [];
                any = false;
                start = ++line;
            }
            else field.Append(ch);
        }
        if (any || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (start, fields);
        }
    }
}

/// <summary>Een gelezen CSV-bestand: de rijen, of waarom het bestand onbruikbaar is.</summary>
public sealed record CsvFile(IReadOnlyList<CsvRow> Rows, string? Error);

/// <summary>Een rij van een CSV-bestand: het regelnummer waarop ze begint en de waarde per kolom van <see cref="CustomerCsv.Columns"/>.</summary>
public sealed record CsvRow(int Line, IReadOnlyDictionary<string, string> Fields)
{
    public string this[string column] => Fields[column];
}
