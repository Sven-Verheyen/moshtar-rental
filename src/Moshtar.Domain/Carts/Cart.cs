using Moshtar.Domain.Common;

namespace Moshtar.Domain.Carts;

/// <summary>
/// De artikelen en pakketten die een klant op de reservatiewebsite verzamelt om ze samen te reserveren, voor één
/// huurperiode. Iets in de winkelkar leggen houdt geen voorraad vast; pas reserveren doet dat.
/// </summary>
public class Cart : TenantEntity
{
    /// <summary>Hoeveel exemplaren van één artikel of pakket een klant hoogstens in zijn winkelkar legt.</summary>
    public const int MaxQuantity = 20;

    /// <summary>Zo lang na de laatste wijziging krijgt een klant die erom vroeg zijn herinnering.</summary>
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromHours(24);

    /// <summary>Een winkelkar die zo lang niet gewijzigd en niet gereserveerd werd, verdwijnt.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>De willekeurige, niet te raden sleutel in de cookie van de klant.</summary>
    public string Key { get; set; } = "";

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateRange Period => new(StartDate, EndDate);

    public List<CartLine> Lines { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>De laatste wijziging; een winkelkar die zo 7 dagen blijft liggen, verdwijnt.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Het e-mailadres van de klant, zodra hij de eerste stap van het reserveren deed.</summary>
    public string? Email { get; set; }

    /// <summary>De taal van de klant, voor de herinneringsmail.</summary>
    public string? Culture { get; set; }

    /// <summary>Wil de klant een herinnering als hij zijn reservatie niet afrondt? Standaard niet.</summary>
    public bool WantsReminder { get; set; }

    /// <summary>Wil de klant nieuws en acties van de verhuurder ontvangen? Standaard niet.</summary>
    public bool WantsNews { get; set; }

    /// <summary>Wanneer alles uit de winkelkar gereserveerd werd; daarna is de winkelkar afgesloten.</summary>
    public DateTime? ReservedAtUtc { get; set; }

    public Guid? ReservationId { get; set; }

    /// <summary>
    /// De sleutel in de link van de herinneringsmail, anders dan die in de cookie: wie de mail ziet, kent zo niet
    /// meteen de sleutel waarmee de klant zijn winkelkar wijzigt. Pas gemaakt als de herinnering vertrekt.
    /// </summary>
    public string? ReminderKey { get; set; }

    /// <summary>Wanneer de herinnering vertrok; er komt er maar één per winkelkar.</summary>
    public DateTime? ReminderSentAtUtc { get; set; }

    /// <summary>Het aantal stuks in de winkelkar, voor het icoon in de kop.</summary>
    public int Count => Lines.Sum(l => l.Quantity);

    public static Cart Create(string key, DateRange period, DateTime atUtc) => new()
    {
        Key = key, StartDate = period.Start, EndDate = period.End, CreatedAtUtc = atUtc, UpdatedAtUtc = atUtc,
    };

    /// <summary>
    /// Legt een artikel of pakket in de winkelkar, voor deze huurperiode. Een andere huurperiode geldt voor de hele
    /// winkelkar. Wat er al in zat, krijgt er exemplaren bij in plaats van een tweede lijn.
    /// </summary>
    public void Add(CatalogRef entry, int quantity, DateRange period, DateTime atUtc)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), "Minstens één exemplaar.");
        (StartDate, EndDate) = (period.Start, period.End);
        if (Lines.FirstOrDefault(l => l.Entry == entry) is { } line)
            line.Quantity = Math.Min(MaxQuantity, line.Quantity + quantity);
        else
            Lines.Add(new CartLine { RentalItemId = entry.RentalItemId, BundleId = entry.BundleId, Quantity = Math.Min(MaxQuantity, quantity) });
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Wijzigt het aantal van een lijn; nul of minder haalt ze uit de winkelkar.</summary>
    public void SetQuantity(CatalogRef entry, int quantity, DateTime atUtc)
    {
        if (Lines.FirstOrDefault(l => l.Entry == entry) is not { } line) return;
        if (quantity < 1) Lines.Remove(line);
        else line.Quantity = Math.Min(MaxQuantity, quantity);
        UpdatedAtUtc = atUtc;
    }

    public void Remove(CatalogRef entry, DateTime atUtc) => SetQuantity(entry, 0, atUtc);

    /// <summary>
    /// De eerste stap van het reserveren: het e-mailadres en de twee toestemmingen. Zo heeft de verhuurder het
    /// e-mailadres ook als de klant daarna afhaakt.
    /// </summary>
    public void Identify(string email, string culture, bool wantsReminder, bool wantsNews, DateTime atUtc)
    {
        Email = email.Trim().ToLowerInvariant();
        Culture = culture;
        WantsReminder = wantsReminder;
        WantsNews = wantsNews;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>Alles uit de winkelkar is gereserveerd: de winkelkar is afgesloten.</summary>
    public void MarkReserved(Guid reservationId, DateTime atUtc)
    {
        ReservationId = reservationId;
        ReservedAtUtc = atUtc;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Krijgt de klant nu zijn herinnering? Alleen als hij erom vroeg, nog niet reserveerde, er nog geen kreeg en
    /// zijn winkelkar 24 uur niet wijzigde. Of er nog iets vrij is, beslist wie de mail stuurt.
    /// </summary>
    public bool IsReminderDue(DateTime nowUtc) =>
        WantsReminder && Email is not null && ReservedAtUtc is null && ReminderSentAtUtc is null
        && UpdatedAtUtc <= nowUtc - ReminderAfter && !IsExpired(nowUtc);

    /// <summary>Niet gereserveerd en 7 dagen niet gewijzigd: de winkelkar mag weg, met het e-mailadres.</summary>
    public bool IsExpired(DateTime nowUtc) => ReservedAtUtc is null && UpdatedAtUtc <= nowUtc - Lifetime;

    /// <summary>Een andere huurperiode voor alles in de winkelkar.</summary>
    public void ChangePeriod(DateRange period, DateTime atUtc)
    {
        (StartDate, EndDate) = (period.Start, period.End);
        UpdatedAtUtc = atUtc;
    }
}

/// <summary>Een lijn in de winkelkar: een los artikel of een pakket, met een aantal.</summary>
public class CartLine
{
    public Guid? RentalItemId { get; set; }
    public Guid? BundleId { get; set; }
    public int Quantity { get; set; } = 1;

    public CatalogRef Entry => new(RentalItemId, BundleId);
}

/// <summary>Een artikel of een pakket, zoals het in een winkelkar of reservatie kan staan.</summary>
public readonly record struct CatalogRef(Guid? RentalItemId, Guid? BundleId)
{
    public static CatalogRef Item(Guid id) => new(id, null);
    public static CatalogRef Bundle(Guid id) => new(null, id);
}
