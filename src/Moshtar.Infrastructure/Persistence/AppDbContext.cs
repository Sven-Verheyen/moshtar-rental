using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Customers;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Identity;

namespace Moshtar.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext) : IdentityUserContext<User, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantHost> TenantHosts => Set<TenantHost>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<RentalItem> RentalItems => Set<RentalItem>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Blockout> Blockouts => Set<Blockout>();
    public DbSet<ReservationEvent> ReservationEvents => Set<ReservationEvent>();

    /// <summary>Wordt per query geëvalueerd door de globale tenantfilter.</summary>
    internal Guid CurrentTenantId => tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Gebruikers horen ook bij één verhuurder, maar erven van IdentityUser in plaats van TenantEntity.
        b.Entity<User>().HasQueryFilter(u => u.TenantId == CurrentTenantId);

        // Elke tenant ziet enkel zijn eigen data.
        foreach (var entityType in b.Model.GetEntityTypes()
                     .Where(t => typeof(TenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(parameter, nameof(TenantEntity.TenantId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenantId))),
                parameter);
            b.Entity(entityType.ClrType).HasQueryFilter(filter);
            b.Entity(entityType.ClrType).HasIndex(nameof(TenantEntity.TenantId));
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = CurrentTenantId != Guid.Empty
                    ? CurrentTenantId
                    : throw new InvalidOperationException("Geen tenant gekend bij het opslaan van tenant-data.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
