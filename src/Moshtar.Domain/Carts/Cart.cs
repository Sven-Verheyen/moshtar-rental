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

    /// <summary>De willekeurige, niet te raden sleutel in de cookie van de klant.</summary>
    public string Key { get; set; } = "";

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateRange Period => new(StartDate, EndDate);

    public List<CartLine> Lines { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>De laatste wijziging; een winkelkar die zo 7 dagen blijft liggen, verdwijnt.</summary>
    public DateTime UpdatedAtUtc { get; set; }

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
