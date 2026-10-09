namespace Moshtar.Domain.Tenants;

/// <summary>Een verhuurbedrijf dat het platform gebruikt. Hopsakee.fun is de eerste verhuurder.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";

    public string DefaultCulture { get; set; } = "nl";
    /// <summary>Komma-gescheiden lijst, bv. "nl,fr,en".</summary>
    public string SupportedCultures { get; set; } = "nl,fr,en";
    public string Currency { get; set; } = "EUR";
    public decimal VatRate { get; set; } = 0.21m;

    /// <summary>Dagen die een artikel voor en na een verhuur geblokkeerd blijft (leveren, poetsen).</summary>
    public int BufferDaysBefore { get; set; }
    public int BufferDaysAfter { get; set; }

    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }

    public List<TenantHost> Hosts { get; set; } = [];

    public IReadOnlyList<string> Cultures =>
        SupportedCultures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Domeinnaam waarop een tenant herkend wordt (bv. "hopsakee.fun").</summary>
public class TenantHost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Hostname { get; set; } = "";
}
