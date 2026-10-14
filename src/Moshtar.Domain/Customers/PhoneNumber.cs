using System.Text.RegularExpressions;

namespace Moshtar.Domain.Customers;

/// <summary>
/// Het telefoonnummer van een klant, zoals Moshtar het bewaart: in internationaal formaat met enkel een + en cijfers
/// (bv. "+32470123456"), zodat het meteen bruikbaar is voor een tel:- of WhatsApp-link.
/// </summary>
public static partial class PhoneNumber
{
    /// <summary>
    /// Het nummer in internationaal formaat, of null als het geen telefoonnummer kan zijn. Spaties, punten, schuine
    /// strepen, haakjes en koppeltekens vallen weg. Een nummer met + of 00 vooraan houdt zijn landcode; een nummer
    /// dat met één 0 begint, is Belgisch en krijgt +32 (bv. "0470 12 34 56" wordt "+32470123456").
    /// </summary>
    public static string? International(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = Separators().Replace(phone, "");
        var international =
            digits.StartsWith('+') ? digits
            : digits.StartsWith("00") ? "+" + digits[2..]
            : BelgianNational().IsMatch(digits) ? "+32" + digits[1..]
            : null;
        return international is not null && InternationalNumber().IsMatch(international) ? international : null;
    }

    [GeneratedRegex("[\\s./()-]")]
    private static partial Regex Separators();

    /// <summary>Een Belgisch nummer zonder landcode: een 0 en 8 (vast) of 9 (gsm) cijfers.</summary>
    [GeneratedRegex("^0[1-9][0-9]{7,8}$")]
    private static partial Regex BelgianNational();

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$")]
    private static partial Regex InternationalNumber();
}
