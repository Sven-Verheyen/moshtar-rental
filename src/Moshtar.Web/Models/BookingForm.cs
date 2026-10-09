using System.ComponentModel.DataAnnotations;
using Moshtar.Domain.Reservations;

namespace Moshtar.Web.Models;

public class BookingForm
{
    [Required] public DateOnly? StartDate { get; set; }
    [Required] public DateOnly? EndDate { get; set; }
    [Range(1, 20)] public int Quantity { get; set; } = 1;

    [Required, MaxLength(100)] public string FirstName { get; set; } = "";
    [Required, MaxLength(100)] public string LastName { get; set; } = "";
    [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = "";
    [MaxLength(30)] public string? Phone { get; set; }

    [Required, MaxLength(200)] public string Street { get; set; } = "";
    [Required, MaxLength(10)] public string PostalCode { get; set; } = "";
    [Required, MaxLength(100)] public string City { get; set; } = "";

    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Delivery;
    [MaxLength(2000)] public string? Notes { get; set; }
}
