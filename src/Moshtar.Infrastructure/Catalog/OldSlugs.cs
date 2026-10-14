using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Catalog;
using Moshtar.Domain.Catalog;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// Houdt de oude slugs bij van categorieën, artikelen en pakketten, per soort en taal. Een beheerservice geeft de
/// slugs van voor en na een wijziging; wat anders werd, stuurt voortaan door naar het nieuwe adres.
/// </summary>
internal sealed class OldSlugs(AppDbContext db, TimeProvider clock) : IOldSlugs
{
    public async Task<Guid?> FindAsync(SlugKind kind, string culture, string slug, CancellationToken ct = default) =>
        await db.OldSlugs.AsNoTracking()
            .Where(s => s.Kind == kind && s.Culture == culture && s.Slug == slug)
            .Select(s => (Guid?)s.TargetId)
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// Bewaart een oude slug voor elke slug die anders werd. Een slug die nu (weer) in gebruik is, is geen oude slug
    /// meer: het huidige adres gaat voor. Bewaart zelf niets; de beheerservice roept daarna SaveChanges op.
    /// </summary>
    /// <param name="before">De slugs van voor de wijziging, per doel en taal.</param>
    /// <param name="after">De slugs van na de wijziging, per doel en taal, van alle doelen van deze soort.</param>
    public async Task RecordAsync(
        SlugKind kind,
        IReadOnlyDictionary<(Guid TargetId, string Culture), string> before,
        IReadOnlyDictionary<(Guid TargetId, string Culture), string> after,
        CancellationToken ct = default)
    {
        var existing = await db.OldSlugs.Where(s => s.Kind == kind).ToListAsync(ct);
        var current = after.Select(kv => (kv.Key.Culture, kv.Value)).ToHashSet();
        db.OldSlugs.RemoveRange(existing.Where(s => current.Contains((s.Culture, s.Slug))));

        foreach (var ((targetId, culture), oldSlug) in before)
        {
            if (after.GetValueOrDefault((targetId, culture)) is not { } newSlug || newSlug == oldSlug) continue;
            if (current.Contains((culture, oldSlug))) continue;
            if (existing.FirstOrDefault(s => s.Culture == culture && s.Slug == oldSlug) is { } known)
            {
                known.TargetId = targetId;
                continue;
            }
            db.OldSlugs.Add(new OldSlug
            {
                Kind = kind, Culture = culture, Slug = oldSlug, TargetId = targetId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            });
        }
    }

    /// <summary>Vergeet de oude slugs van iets wat verwijderd wordt: hun adres toont voortaan een 404.</summary>
    public Task ForgetAsync(SlugKind kind, Guid targetId, CancellationToken ct = default) =>
        db.OldSlugs.Where(s => s.Kind == kind && s.TargetId == targetId).ExecuteDeleteAsync(ct);
}
