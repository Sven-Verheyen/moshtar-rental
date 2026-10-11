using Moshtar.Domain.Common;

namespace Moshtar.Domain.Marketing;

/// <summary>
/// Een e-mailadres van een klant of bezoeker die uitdrukkelijk toestemming gaf om nieuws en acties van de verhuurder
/// te ontvangen, met zijn taal en het tijdstip van die toestemming. Uniek per verhuurder; blijft bestaan als de
/// winkelkar waarin de toestemming gegeven werd, verdwijnt.
/// </summary>
public class MarketingContact : TenantEntity
{
    public string Email { get; set; } = "";
    public string Culture { get; set; } = "nl";

    /// <summary>De laatste keer dat de klant toestemming gaf; opnieuw toestemming geven werkt dit bij.</summary>
    public DateTime ConsentedAtUtc { get; set; }

    public MarketingContactSource Source { get; set; }
}

/// <summary>Waar de klant zijn toestemming gaf.</summary>
public enum MarketingContactSource
{
    /// <summary>In de eerste stap van het reserveren, in de winkelkar.</summary>
    Cart,

    /// <summary>De klant reserveerde ook, met het vinkje aan.</summary>
    Reservation,
}
