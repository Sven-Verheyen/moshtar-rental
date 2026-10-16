using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;

namespace Moshtar.Application.Customers;

/// <summary>Het klantenbestand van de huidige verhuurder, voor beheerder en medewerker.</summary>
public interface ICustomerAdministration
{
    /// <summary>
    /// De klanten die passen bij de zoektekst (naam, bedrijfsnaam, e-mailadres, telefoon of gemeente) en het type,
    /// op naam gesorteerd, één pagina tegelijk.
    /// </summary>
    Task<CustomerPage> SearchAsync(CustomerSearch search, CancellationToken ct = default);

    /// <summary>De klantfiche, of null als de klant niet (meer) bestaat.</summary>
    Task<CustomerCard?> GetAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>Voegt een klant toe namens deze gebruiker; geweigerd als de gegevens niet kloppen of het e-mailadres al klant is.</summary>
    Task<CustomerSaveResult> AddAsync(CustomerDetails details, Guid userId, CancellationToken ct = default);

    /// <summary>Wijzigt een klant namens deze gebruiker; geweigerd als de gegevens niet kloppen of het e-mailadres van een andere klant is.</summary>
    Task<CustomerSaveResult> UpdateAsync(Guid customerId, CustomerDetails details, Guid userId, CancellationToken ct = default);
}

/// <param name="Page">De pagina, vanaf 0.</param>
public sealed record CustomerSearch(string? Text = null, CustomerKind? Kind = null, int Page = 0, int PageSize = 25);

public sealed record CustomerPage(IReadOnlyList<CustomerListItem> Items, int TotalCount);

/// <summary>
/// Een klant in de lijst. <see cref="ReservationCount"/> en <see cref="LastPeriod"/> tellen alleen reservaties die
/// niet geannuleerd zijn.
/// </summary>
public sealed record CustomerListItem(
    Guid Id, CustomerKind Kind, string FirstName, string LastName, string? CompanyName, string Email, string? Phone, string City,
    int ReservationCount, DateRange? LastPeriod);

/// <summary>
/// De klantfiche: de klant, al zijn reservaties (ook geannuleerde, de laatste huurperiode eerst), of hij marketingcontact
/// is en of hij zich afmeldde voor reviewvragen. <see cref="CreatedBy"/> en <see cref="UpdatedBy"/> zijn het
/// e-mailadres van de gebruiker, of null als het via de website gebeurde.
/// </summary>
public sealed record CustomerCard(
    Customer Customer, IReadOnlyList<CustomerReservation> Reservations, bool IsMarketingContact, bool OptedOutOfReviewRequests,
    string? CreatedBy, string? UpdatedBy);

public sealed record CustomerReservation(Guid Id, string Number, DateRange Period, ReservationStatus Status, decimal TotalPrice);

/// <summary>
/// De uitkomst van toevoegen of wijzigen: de klant, of waarom het niet kon. Is het e-mailadres al van een andere klant,
/// dan staat die in <see cref="ExistingCustomerId"/>, zodat het back office ernaar kan linken.
/// </summary>
public sealed record CustomerSaveResult(Guid? CustomerId, IReadOnlyList<string> Errors, Guid? ExistingCustomerId = null)
{
    public bool Succeeded => CustomerId is not null;
}
