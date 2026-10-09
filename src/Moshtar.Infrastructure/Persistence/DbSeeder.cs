using Microsoft.EntityFrameworkCore;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Tenants;

namespace Moshtar.Infrastructure.Persistence;

/// <summary>Demo-data voor lokale ontwikkeling: de verhuurder Hopsakee.fun met enkele artikelen en een pakket.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(ct)) return;

        var tenant = new Tenant
        {
            Name = "Hopsakee.fun",
            Slug = "hopsakee",
            DefaultCulture = "nl",
            SupportedCultures = "nl,fr,en",
            BufferDaysAfter = 0,
            Hosts = [new TenantHost { Hostname = "localhost" }, new TenantHost { Hostname = "hopsakee.fun" }, new TenantHost { Hostname = "www.hopsakee.fun" }],
        };
        foreach (var h in tenant.Hosts) h.TenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var castles = new Category
        {
            TenantId = tenant.Id, Slug = "springkastelen", SortOrder = 1,
            Translations = [T("nl", "Springkastelen"), T("fr", "Châteaux gonflables"), T("en", "Bouncy castles")],
        };
        var games = new Category
        {
            TenantId = tenant.Id, Slug = "spellen", SortOrder = 2,
            Translations = [T("nl", "Spellen"), T("fr", "Jeux"), T("en", "Games")],
        };

        var jungle = new RentalItem
        {
            TenantId = tenant.Id, Slug = "springkasteel-jungle", Category = castles, Stock = 1,
            Pricing = new Pricing { DayPrice = 150m, ExtraDayPrice = 75m },
            LengthCm = 500, WidthCm = 400, HeightCm = 350, MinAge = 3, MaxAge = 12, MaxPersons = 8, RequiresPower = true,
            Translations =
            [
                T("nl", "Springkasteel Jungle", "Groot springkasteel met glijbaan in junglethema."),
                T("fr", "Château gonflable Jungle", "Grand château gonflable avec toboggan, thème jungle."),
                T("en", "Jungle bouncy castle", "Large bouncy castle with slide in a jungle theme."),
            ],
        };
        var princess = new RentalItem
        {
            TenantId = tenant.Id, Slug = "springkasteel-prinses", Category = castles, Stock = 2,
            Pricing = new Pricing { DayPrice = 120m, ExtraDayPrice = 60m },
            LengthCm = 400, WidthCm = 400, HeightCm = 300, MinAge = 3, MaxAge = 10, MaxPersons = 6, RequiresPower = true,
            Translations =
            [
                T("nl", "Springkasteel Prinses", "Roze springkasteel voor kleine prinsen en prinsessen."),
                T("fr", "Château gonflable Princesse", "Château gonflable rose pour petits princes et princesses."),
                T("en", "Princess bouncy castle", "Pink bouncy castle for little princes and princesses."),
            ],
        };
        var cornhole = new RentalItem
        {
            TenantId = tenant.Id, Slug = "cornhole", Category = games, Stock = 4,
            Pricing = new Pricing { DayPrice = 25m, ExtraDayPrice = 10m },
            MinAge = 6,
            Translations = [T("nl", "Cornhole"), T("fr", "Cornhole"), T("en", "Cornhole")],
        };
        var fourInARow = new RentalItem
        {
            TenantId = tenant.Id, Slug = "reuze-vier-op-een-rij", Category = games, Stock = 2,
            Pricing = new Pricing { DayPrice = 30m, ExtraDayPrice = 15m },
            MinAge = 5,
            Translations = [T("nl", "Reuze vier op een rij"), T("fr", "Puissance 4 géant"), T("en", "Giant four in a row")],
        };
        db.RentalItems.AddRange(jungle, princess, cornhole, fourInARow);

        db.Bundles.Add(new Bundle
        {
            TenantId = tenant.Id, Slug = "kinderfeest-xl",
            Pricing = new Pricing { DayPrice = 190m, ExtraDayPrice = 95m },
            Items =
            [
                new BundleItem { RentalItemId = jungle.Id, Quantity = 1 },
                new BundleItem { RentalItemId = cornhole.Id, Quantity = 1 },
                new BundleItem { RentalItemId = fourInARow.Id, Quantity = 1 },
            ],
            Translations =
            [
                T("nl", "Kinderfeest XL", "Springkasteel Jungle met cornhole en reuze vier op een rij."),
                T("fr", "Fête d'enfants XL", "Château Jungle avec cornhole et puissance 4 géant."),
                T("en", "Kids party XL", "Jungle bouncy castle with cornhole and giant four in a row."),
            ],
        });

        await db.SaveChangesAsync(ct);
    }

    private static Translation T(string culture, string name, string? description = null) =>
        new() { Culture = culture, Name = name, Description = description };
}
