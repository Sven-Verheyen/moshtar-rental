using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Infrastructure.Persistence;

internal class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(t => t.Name).HasMaxLength(200);
        b.Property(t => t.Slug).HasMaxLength(100);
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.VatRate).HasPrecision(5, 4);
        b.Ignore(t => t.Cultures);
        b.HasMany(t => t.Hosts).WithOne().HasForeignKey(h => h.TenantId);
    }
}

internal class TenantHostConfiguration : IEntityTypeConfiguration<TenantHost>
{
    public void Configure(EntityTypeBuilder<TenantHost> b)
    {
        b.Property(h => h.Hostname).HasMaxLength(253);
        b.HasIndex(h => h.Hostname).IsUnique();
    }
}

internal class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique();
        b.OwnsMany(c => c.Translations, t => t.ToJson());
    }
}

internal class RentalItemConfiguration : IEntityTypeConfiguration<RentalItem>
{
    public void Configure(EntityTypeBuilder<RentalItem> b)
    {
        b.HasIndex(i => new { i.TenantId, i.Slug }).IsUnique();
        b.OwnsOne(i => i.Pricing, p =>
        {
            p.Property(x => x.DayPrice).HasColumnName("DayPrice").HasPrecision(10, 2);
            p.Property(x => x.ExtraDayPrice).HasColumnName("ExtraDayPrice").HasPrecision(10, 2);
        });
        b.OwnsMany(i => i.Translations, t => t.ToJson());
        b.OwnsMany(i => i.Images, t => t.ToJson());
        b.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal class BundleConfiguration : IEntityTypeConfiguration<Bundle>
{
    public void Configure(EntityTypeBuilder<Bundle> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        b.OwnsOne(x => x.Pricing, p =>
        {
            p.Property(x => x.DayPrice).HasColumnName("DayPrice").HasPrecision(10, 2);
            p.Property(x => x.ExtraDayPrice).HasColumnName("ExtraDayPrice").HasPrecision(10, 2);
        });
        b.OwnsMany(x => x.Translations, t => t.ToJson());
        b.OwnsMany(x => x.Items, i =>
        {
            i.ToTable("BundleItems");
            i.WithOwner().HasForeignKey("BundleId");
            i.HasKey("BundleId", nameof(BundleItem.RentalItemId));
            i.HasOne<RentalItem>().WithMany().HasForeignKey(x => x.RentalItemId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(c => c.Email).HasMaxLength(320);
        b.HasIndex(c => new { c.TenantId, c.Email });
        b.OwnsOne(c => c.Address);
    }
}

internal class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.Property(r => r.Number).HasMaxLength(20);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        b.HasIndex(r => new { r.TenantId, r.StartDate, r.EndDate });
        b.Property(r => r.TotalPrice).HasPrecision(10, 2);
        b.Ignore(r => r.Period);
        b.Ignore(r => r.OccupiesStock);
        b.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsOne(r => r.DeliveryAddress);
        b.OwnsMany(r => r.Lines, l =>
        {
            l.ToTable("ReservationLines");
            l.WithOwner().HasForeignKey("ReservationId");
            l.HasKey(x => x.Id);
            l.Property(x => x.UnitPrice).HasPrecision(10, 2);
            l.Ignore(x => x.LineTotal);
            l.HasOne<RentalItem>().WithMany().HasForeignKey(x => x.RentalItemId).OnDelete(DeleteBehavior.Restrict);
            l.HasOne<Bundle>().WithMany().HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal class BlockoutConfiguration : IEntityTypeConfiguration<Blockout>
{
    public void Configure(EntityTypeBuilder<Blockout> b)
    {
        b.HasOne<RentalItem>().WithMany().HasForeignKey(x => x.RentalItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId);

        // Identity maakt e-mail en gebruikersnaam uniek over het hele platform; wij enkel per verhuurder.
        b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique(false);
        b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique();
        b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
    }
}
