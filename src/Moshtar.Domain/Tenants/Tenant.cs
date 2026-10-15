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

    // Bedrijfsgegevens, zoals klanten ze op de reservatiewebsite zien.
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    /// <summary>
    /// Het punt van het bedrijfsadres op de kaart: daar vertrekt en eindigt elke ronde. Null zolang het niet opgezocht
    /// of niet gevonden is; het wordt opnieuw opgezocht als het adres verandert.
    /// </summary>
    public Moshtar.Domain.Common.GeoPoint? AddressPoint { get; set; }
    public string? WhatsAppPhone { get; set; }
    public string? VatNumber { get; set; }
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? TikTokUrl { get; set; }
    /// <summary>Het Google-bedrijfsprofiel, waar klanten de reviews lezen.</summary>
    public string? GoogleBusinessUrl { get; set; }

    /// <summary>Het GA4-meet-ID (G-…). Zonder: geen Google Analytics en geen toestemmingsbalk op de reservatiewebsite.</summary>
    public string? GoogleAnalyticsId { get; set; }
    /// <summary>Het werkgebied: de gemeenten waar de verhuurder levert.</summary>
    public List<string> ServiceArea { get; set; } = [];
    /// <summary>Kunnen klanten zelf afhalen? Zo niet, dan levert de verhuurder altijd en is er geen keuze.</summary>
    public bool OffersPickup { get; set; }

    // Standaarden voor de rondes in de Planning (zie RoundSettings).
    public TimeOnly DeliveryDeparture { get; set; } = Rounds.RoundSettings.Default.DeliveryDeparture;
    public TimeOnly CollectionDeparture { get; set; } = Rounds.RoundSettings.Default.CollectionDeparture;
    public int SetupMinutes { get; set; } = Rounds.RoundSettings.Default.SetupMinutes;
    public int TeardownMinutes { get; set; } = Rounds.RoundSettings.Default.TeardownMinutes;
    public int WindowMinutes { get; set; } = Rounds.RoundSettings.Default.WindowMinutes;

    /// <summary>De standaarden voor de rondes van deze verhuurder.</summary>
    public Rounds.RoundSettings RoundSettings
    {
        get => new(DeliveryDeparture, CollectionDeparture, SetupMinutes, TeardownMinutes, WindowMinutes);
        set => (DeliveryDeparture, CollectionDeparture, SetupMinutes, TeardownMinutes, WindowMinutes) =
            (value.DeliveryDeparture, value.CollectionDeparture, value.SetupMinutes, value.TeardownMinutes, value.WindowMinutes);
    }

    /// <summary>Sleutel van het logo in de afbeeldingenopslag (ADR 0005).</summary>
    public string? LogoImageKey { get; set; }
    public string? PrimaryColor { get; set; }
    /// <summary>Sleutel van de sfeerfoto in de afbeeldingenopslag, groot bovenaan de home van de reservatiewebsite.</summary>
    public string? HeroImageKey { get; set; }

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
