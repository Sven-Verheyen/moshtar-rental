using System.ComponentModel.DataAnnotations;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Models;

/// <summary>Stap 2 van het reserveren vanuit de winkelkar; huurperiode, artikelen en e-mailadres staan al op de winkelkar.</summary>
public class CartReservationForm
{
    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    [MaxLength(30)] public string? Phone { get; set; }

    [Required, MaxLength(200)] public string Street { get; set; } = "";
    [Required, MaxLength(10)] public string PostalCode { get; set; } = "";
    [Required, MaxLength(100)] public string City { get; set; } = "";

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    [MaxLength(2000)] public string? Notes { get; set; }
}
