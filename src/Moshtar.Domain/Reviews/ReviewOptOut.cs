using Moshtar.Domain.Common;

namespace Moshtar.Domain.Reviews;

/// <summary>
/// Een e-mailadres dat zich bij een verhuurder afmeldde voor reviewvragen, met het tijdstip. Dat adres krijgt bij die
/// verhuurder nooit meer een reviewvraag, per mail noch via WhatsApp; reservatiemails en een toestemming als
/// marketingcontact blijven ongewijzigd. Uniek per verhuurder.
/// </summary>
public class ReviewOptOut : TenantEntity
{
    /// <summary>Het e-mailadres zoals <see cref="Normalize"/> het schrijft.</summary>
    public string Email { get; set; } = "";

    public DateTime OptedOutAtUtc { get; set; }

    /// <summary>Een e-mailadres zoals het vergeleken wordt: zonder spaties rond en in kleine letters, zoals bij de klant.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
