namespace Moshtar.Application.Reviews;

/// <summary>
/// De reviewvragen van de huidige verhuurder, op de achtergrond gemaild. Staat "Reviewvragen sturen" aan, dan krijgt
/// elke niet-geannuleerde reservatie die eindigde na het aanzetten, de dag na haar einddag vanaf 10u Belgische tijd
/// één reviewvraag, tot 3 dagen na de einddag. Reservaties van dezelfde klant (e-mailadres) met dezelfde einddag
/// krijgen samen één mail; elke reservatie krijgt een lijn in de historiek. Komt er later nog een reservatie bij voor
/// dezelfde klant en einddag, dan krijgt die alleen de lijn in de historiek, geen tweede mail (zie
/// <see cref="Domain.Reviews.ReviewRequestRule"/>). Er vertrekt nooit een tweede, ook niet na
/// een herstart of als het versturen twee keer tegelijk loopt; een mail die mislukte, wordt bij een volgende run
/// opnieuw geprobeerd.
/// </summary>
public interface IReviewRequests
{
    /// <summary>Verstuurt wat op dit moment moet vertrekken. Geeft het aantal verstuurde mails.</summary>
    Task<int> SendDueAsync(DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>Het publieke adres van de QR-code van de reviewlink, op het hoofddomein van de verhuurder.</summary>
public static class ReviewQrCodePath
{
    public const string Path = "/review-qr.png";
}
