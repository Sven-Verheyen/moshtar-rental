namespace Moshtar.Domain.Customers;

/// <summary>Of een klant als privépersoon huurt of namens een bedrijf of organisatie.</summary>
public enum CustomerKind
{
    /// <summary>Particuliere klant: huurt als privépersoon.</summary>
    Private,

    /// <summary>
    /// Zakelijke klant: een bedrijf of organisatie (school, vereniging, gemeente) met een bedrijfsnaam, eventueel een
    /// btw-nummer, en een contactpersoon.
    /// </summary>
    Business,
}
