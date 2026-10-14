using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Moshtar.Domain.Carts;
using Moshtar.Domain.Marketing;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Domain.Website;
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
        b.Property(t => t.SenderEmail).HasMaxLength(254);
        b.Property(t => t.Street).HasMaxLength(200);
        b.Property(t => t.PostalCode).HasMaxLength(20);
        b.Property(t => t.City).HasMaxLength(100);
        b.Property(t => t.WhatsAppPhone).HasMaxLength(30);
        b.Property(t => t.VatNumber).HasMaxLength(30);
        b.Property(t => t.FacebookUrl).HasMaxLength(500);
        b.Property(t => t.InstagramUrl).HasMaxLength(500);
        b.Property(t => t.TikTokUrl).HasMaxLength(500);
        b.Property(t => t.GoogleBusinessUrl).HasMaxLength(500);
        b.Property(t => t.GoogleAnalyticsId).HasMaxLength(30);
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
        // Expliciet, anders vervangt EF de index van de foreign key door de gefilterde hieronder.
        b.HasIndex(h => h.TenantId);
        // Hoogstens één hoofddomein per verhuurder.
        b.HasIndex(h => h.TenantId, "IX_TenantHosts_PrimaryHost").IsUnique().HasFilter("\"IsPrimary\"");
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
        b.OwnsMany(i => i.Photos, t => t.ToJson());
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

internal class ReservationEventConfiguration : IEntityTypeConfiguration<ReservationEvent>
{
    public void Configure(EntityTypeBuilder<ReservationEvent> b)
    {
        b.ToTable("ReservationEvents");
        // Gebeurtenissen krijgen hun Id in de code; zo ziet EF ze als nieuw wanneer ze aan History toegevoegd worden.
        b.Property(e => e.Id).ValueGeneratedNever();
        b.HasOne<Reservation>().WithMany(r => r.History).HasForeignKey(e => e.ReservationId).OnDelete(DeleteBehavior.Cascade);
        // Gebruikers worden uitgeschakeld, nooit verwijderd: de historiek blijft naar hen verwijzen.
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ReservationId, e.OccurredAtUtc });
        b.Property(e => e.Note).HasMaxLength(ReservationEvent.MaxNoteLength);
    }
}

internal class AlternativeProposalConfiguration : IEntityTypeConfiguration<AlternativeProposal>
{
    public void Configure(EntityTypeBuilder<AlternativeProposal> b)
    {
        b.ToTable("AlternativeProposals");
        b.Property(p => p.Surcharge).HasPrecision(10, 2);
        b.HasOne<Reservation>().WithMany().HasForeignKey(p => p.ReservationId).OnDelete(DeleteBehavior.Cascade);
        // Een artikel dat ooit gereserveerd werd, wordt niet verwijderd; een voorgesteld alternatief mag wel weg.
        b.HasOne<RentalItem>().WithMany().HasForeignKey(p => p.RentalItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<RentalItem>().WithMany().HasForeignKey(p => p.AlternativeRentalItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.ReservationId, p.RentalItemId, p.AlternativeRentalItemId }).IsUnique();
    }
}

internal class BlockoutConfiguration : IEntityTypeConfiguration<Blockout>
{
    public void Configure(EntityTypeBuilder<Blockout> b)
    {
        b.HasOne<RentalItem>().WithMany().HasForeignKey(x => x.RentalItemId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Reason).HasMaxLength(200);
    }
}

internal class MailTemplateCustomizationConfiguration : IEntityTypeConfiguration<MailTemplateCustomization>
{
    public void Configure(EntityTypeBuilder<MailTemplateCustomization> b)
    {
        b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(40);
        b.Property(t => t.Culture).HasMaxLength(10);
        b.Property(t => t.Subject).HasMaxLength(300);
        b.Ignore(t => t.Text);
        b.HasIndex(t => new { t.TenantId, t.Kind, t.Culture }).IsUnique();
        // Gebruikers worden uitgeschakeld, nooit verwijderd.
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.IsActive).HasDefaultValue(true);
        b.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId);

        // Identity maakt e-mail en gebruikersnaam uniek over het hele platform; wij enkel per verhuurder.
        b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique(false);
        b.HasIndex(u => new { u.TenantId, u.NormalizedUserName }).IsUnique();
        b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
    }
}

internal class WebsiteTextCustomizationConfiguration : IEntityTypeConfiguration<WebsiteTextCustomization>
{
    public void Configure(EntityTypeBuilder<WebsiteTextCustomization> b)
    {
        b.Property(t => t.Key).HasConversion<string>().HasMaxLength(40);
        b.Property(t => t.Culture).HasMaxLength(10);
        // Eén aanpassing per tekst, taal en categorie; NULLS NOT DISTINCT zodat dat ook geldt voor teksten zonder categorie.
        b.HasIndex(t => new { t.TenantId, t.Culture, t.Key, t.CategoryId }).IsUnique().AreNullsDistinct(false);
        b.HasOne<Category>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Cascade);
        // Gebruikers worden uitgeschakeld, nooit verwijderd.
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class OldSlugConfiguration : IEntityTypeConfiguration<OldSlug>
{
    public void Configure(EntityTypeBuilder<OldSlug> b)
    {
        b.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Culture).HasMaxLength(10);
        b.Property(s => s.Slug).HasMaxLength(CategorySlug.MaxLength);
        // Een oude slug wijst per soort en taal naar precies één categorie, artikel of pakket.
        b.HasIndex(s => new { s.TenantId, s.Kind, s.Culture, s.Slug }).IsUnique();
        b.HasIndex(s => new { s.TenantId, s.Kind, s.TargetId });
    }
}

internal class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.Property(c => c.Key).HasMaxLength(64);
        b.Property(c => c.Email).HasMaxLength(320);
        b.Property(c => c.Culture).HasMaxLength(10);
        b.Property(c => c.ReminderKey).HasMaxLength(64);
        b.HasIndex(c => c.Key).IsUnique();
        b.HasIndex(c => c.ReminderKey).IsUnique();
        b.HasIndex(c => c.UpdatedAtUtc);
        b.Ignore(c => c.Period);
        b.Ignore(c => c.Count);
        b.OwnsMany(c => c.Lines, l =>
        {
            l.ToJson();
            l.Ignore(x => x.Entry);
        });
    }
}

internal sealed class MarketingContactConfiguration : IEntityTypeConfiguration<MarketingContact>
{
    public void Configure(EntityTypeBuilder<MarketingContact> b)
    {
        b.Property(c => c.Email).HasMaxLength(320);
        b.Property(c => c.Culture).HasMaxLength(10);
        b.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
    }
}
