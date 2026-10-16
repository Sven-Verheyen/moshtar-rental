using Microsoft.EntityFrameworkCore;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Npgsql;

namespace Moshtar.Infrastructure.Customers;

/// <summary>Wat het beheer van klanten en de import en export van het klantenbestand allebei controleren.</summary>
internal static class CustomerChecks
{
    public const string DuplicateEmail = "Er is al een klant met dit e-mailadres.";

    /// <summary>
    /// Is dit een actieve beheerder? Alleen hij mag klanten verwijderen, anonimiseren en importeren, ook al toont het
    /// back office die knoppen enkel aan hem.
    /// </summary>
    public static Task<bool> IsAdministratorAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Administrator, ct);

    /// <summary>Weigerde de databank het bewaren omdat een unieke sleutel, zoals het e-mailadres, al bestond?</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
