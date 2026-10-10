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

    /// <summary>
    /// Adres op het eigen domein waarvan mails vertrekken, bv. noreply@hopsakee.fun.
    /// Het domein moet eerst geverifieerd zijn in Azure Communication Services.
    /// </summary>
    public string? SenderEmail { get; set; }
    /// <summary>Contactadres van de verhuurder; ook het antwoordadres van zijn mails.</summary>
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    /// <summary>Adres van de sfeerfoto, groot bovenaan de home van de reservatiewebsite.</summary>
    public string? HeroImageUrl { get; set; }

    public List<TenantHost> Hosts { get; set; } = [];

    public IReadOnlyList<string> Cultures =>
        SupportedCultures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Het hoofddomein: het enige domein waarop de reservatiewebsite getoond wordt, of null als er geen gekozen is.</summary>
    public string? PrimaryHost => Hosts.FirstOrDefault(h => h.IsPrimary)?.Hostname;
}

/// <summary>Domeinnaam waarop een tenant herkend wordt (bv. "hopsakee.fun").</summary>
public class TenantHost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Hostname { get; set; } = "";

    /// <summary>Of dit het hoofddomein van de verhuurder is. Zijn andere domeinen sturen door naar het hoofddomein.</summary>
    public bool IsPrimary { get; set; }
}
