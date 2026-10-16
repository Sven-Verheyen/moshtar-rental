using System.ComponentModel.DataAnnotations;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Models;

/// <summary>Stap 2 van het reserveren vanuit de winkelkar; huurperiode, artikelen en e-mailadres staan al op de winkelkar.</summary>
public class CartReservationForm
{
    /// <summary>Particulier (standaard) of bedrijf of organisatie.</summary>
    public CustomerKind Kind { get; set; } = CustomerKind.Private;
    /// <summary>Verplicht voor een zakelijke klant; de winkelkar meldt het in de taal van de klant.</summary>
    [MaxLength(200)] public string? CompanyName { get; set; }
    /// <summary>Optioneel; alleen het formaat wordt gecontroleerd.</summary>
    [MaxLength(30)] public string? VatNumber { get; set; }

    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    /// <summary>Verplicht; de winkelkar weigert een leeg of ongeldig nummer met een melding in de taal van de klant.</summary>
    [MaxLength(30)] public string? Phone { get; set; }

    [Required, MaxLength(200)] public string Street { get; set; } = "";
    [Required, MaxLength(10)] public string PostalCode { get; set; } = "";
    [Required, MaxLength(100)] public string City { get; set; } = "";

    /// <summary>
    /// "Factuuradres is hetzelfde". Zonder standaardwaarde: een vinkje dat uit staat, wordt niet meegestuurd, dus een
    /// verstuurd formulier zonder dit veld betekent "uit". Een nieuw formulier zet het zelf aan (<see cref="New"/>).
    /// </summary>
    public bool SameBillingAddress { get; set; }
    [MaxLength(200)] public string? BillingStreet { get; set; }
    [MaxLength(10)] public string? BillingPostalCode { get; set; }
    [MaxLength(100)] public string? BillingCity { get; set; }

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    [MaxLength(2000)] public string? Notes { get; set; }

    /// <summary>
    /// Verstuurd door de knop die alleen het formulier bijwerkt na een andere keuze voor het type klant of het factuuradres,
    /// zonder JavaScript: dan wordt er niet gereserveerd en verschijnen de juiste velden, met wat al ingevuld was.
    /// </summary>
    public string? Refresh { get; set; }

    /// <summary>Het formulier zoals de klant het eerst ziet: particulier, met "Factuuradres is hetzelfde" aangevinkt.</summary>
    public static CartReservationForm New() => new() { SameBillingAddress = true };

    /// <summary>Het aparte factuuradres, of null als het hetzelfde is als het adres.</summary>
    public Address? BillingAddress => SameBillingAddress
        ? null
        : new Address { Street = BillingStreet?.Trim() ?? "", PostalCode = BillingPostalCode?.Trim() ?? "", City = BillingCity?.Trim() ?? "" };
}
