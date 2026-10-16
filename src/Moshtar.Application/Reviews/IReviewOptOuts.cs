namespace Moshtar.Application.Reviews;

/// <summary>
/// De e-mailadressen die zich bij de huidige verhuurder afmeldden voor reviewvragen. Een afgemeld adres krijgt geen
/// reviewvraag meer, per mail noch via WhatsApp. Afmelden verandert niets aan reservatiemails of marketingcontacten.
/// Hoofdletters en spaties rond het adres tellen niet.
/// </summary>
public interface IReviewOptOuts
{
    /// <summary>Meldt dit adres af; een tweede keer afmelden houdt het eerste tijdstip.</summary>
    Task OptOutAsync(string email, CancellationToken ct = default);

    Task<bool> IsOptedOutAsync(string email, CancellationToken ct = default);

    /// <summary>De adressen uit deze lijst die afgemeld zijn, zoals <see cref="Domain.Reviews.ReviewOptOut.Normalize"/> ze schrijft.</summary>
    Task<IReadOnlySet<string>> OptedOutAmongAsync(IEnumerable<string> emails, CancellationToken ct = default);
}

/// <summary>
/// De afmeldlink onderaan een reviewvraag: een pagina van de reservatiewebsite in de taal van de mail, met een
/// ondertekend token (verhuurder + e-mailadres), zodat niemand een ander adres of bij een andere verhuurder afmeldt.
/// </summary>
public interface IReviewOptOutLinks
{
    /// <summary>De volledige afmeldlink voor dit adres bij de huidige verhuurder, op dit domein en in deze taal.</summary>
    string For(string host, string culture, string email);

    /// <summary>Een link naar de afmeldpagina die niemand afmeldt, voor het voorbeeld en de testmail.</summary>
    string Example(string host, string culture);
}
