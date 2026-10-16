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

    /// <summary>
    /// Wijzigt een klant namens deze gebruiker; geweigerd als de klant niet (meer) bestaat, de gegevens niet kloppen of
    /// het e-mailadres van een andere klant is.
    /// </summary>
    Task<CustomerSaveResult> UpdateAsync(Guid customerId, CustomerDetails details, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Wat de btw-databank zegt over een btw-nummer dat een gebruiker ingeeft, zodat het formulier bedrijfsnaam en
    /// factuuradres kan invullen. Een nummer met een verkeerd formaat of geen nummer gaat niet naar de databank (status
    /// null), een nummer van buiten de EU evenmin (niet te controleren). Bewaart niets.
    /// </summary>
    Task<VatNumberLookup> LookUpVatNumberAsync(string? vatNumber, CancellationToken ct = default);

    /// <summary>
    /// "Opnieuw controleren": vraagt de btw-databank opnieuw naar het EU-btw-nummer van de klant en bewaart het
    /// antwoord, ook als het nummer nu ongeldig is. Is ze onbereikbaar, dan blijft een eerdere uitspraak staan.
    /// </summary>
    Task<VatRecheckResult> RecheckVatNumberAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Verwijdert een klant zonder reservaties, namens deze gebruiker. Alleen een beheerder mag dat; een klant met
    /// reservaties wordt geanonimiseerd in plaats van verwijderd.
    /// </summary>
    Task<CustomerRemovalResult> DeleteAsync(Guid customerId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Anonimiseert een klant met reservaties namens deze gebruiker, bijvoorbeeld op vraag van de klant: zijn
    /// persoonsgegevens worden gewist, zijn reservaties blijven voor de cijfers zonder te tonen van wie ze waren. Het
    /// marketingcontact van zijn e-mailadres wordt verwijderd; zijn afmelding voor reviewvragen blijft. Alleen een
    /// beheerder mag dat, en niet zolang een reservatie van de klant nog loopt.
    /// </summary>
    Task<CustomerRemovalResult> AnonymizeAsync(Guid customerId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Wat de btw-databank over een ingegeven btw-nummer zegt; voor een geldig nummer de officiële naam en het adres (zo
/// goed als het uit de tekst van de databank te halen is), als ze die geeft.
/// </summary>
public sealed record VatNumberLookup(VatCheckStatus? Status, string? Name = null, Address? Address = null);

/// <summary>De uitkomst van "Opnieuw controleren": de status van het nummer nu, en waarom het niet kon of niets veranderde.</summary>
public sealed record VatRecheckResult(VatCheckStatus? Status, string? Message = null);

/// <summary>De uitkomst van verwijderen of anonimiseren: gelukt, of waarom het niet kon.</summary>
public sealed record CustomerRemovalResult(IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
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
    int ReservationCount, DateRange? LastPeriod, VatCheckStatus? VatCheck = null)
{
    public bool IsAnonymized => Customer.IsAnonymizedEmail(Email);
}

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
