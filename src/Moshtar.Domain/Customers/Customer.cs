using Moshtar.Domain.Common;

namespace Moshtar.Domain.Customers;

public class Customer : TenantEntity
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string? VatNumber { get; set; }
    public Address Address { get; set; } = new();
    public string PreferredCulture { get; set; } = "nl";
}
