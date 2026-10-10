using System.ComponentModel.DataAnnotations;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Models;

/// <summary>Een reservatie die een gebruiker in het back office invoert.</summary>
public class ManualReservationForm
{
    [Required(ErrorMessage = "Kies een begindatum.")] public DateTime? StartDate { get; set; }
    [Required(ErrorMessage = "Kies een einddatum.")] public DateTime? EndDate { get; set; }

    public List<ManualReservationLine> Lines { get; set; } = [new()];

    [Required(ErrorMessage = "Vul de voornaam in."), MaxLength(100)] public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "Vul de achternaam in."), MaxLength(100)] public string LastName { get; set; } = "";
    [Required(ErrorMessage = "Vul het e-mailadres in."), EmailAddress(ErrorMessage = "Dit is geen geldig e-mailadres."), MaxLength(320)]
    public string Email { get; set; } = "";
    [MaxLength(30)] public string? Phone { get; set; }

    [Required(ErrorMessage = "Vul de straat en het huisnummer in."), MaxLength(200)] public string Street { get; set; } = "";
    [Required(ErrorMessage = "Vul de postcode in."), MaxLength(10)] public string PostalCode { get; set; } = "";
    [Required(ErrorMessage = "Vul de gemeente in."), MaxLength(100)] public string City { get; set; } = "";

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    [MaxLength(2000)] public string? Notes { get; set; }
    /// <summary>De taal van de klant, voor de bevestigingsmail.</summary>
    public string Culture { get; set; } = "nl";
}

public class ManualReservationLine
{
    /// <summary>Het gekozen artikel of pakket, als "item:{id}" of "bundle:{id}".</summary>
    public string? Choice { get; set; }
    public int Quantity { get; set; } = 1;
}
