using Moshtar.Domain.Common;

namespace Moshtar.Application.Carts;

/// <summary>De winkelkarren van de huidige verhuurder in het back office, alleen lezen.</summary>
public interface ICartAdministration
{
    /// <summary>De winkelkarren die niet gereserveerd zijn, de laatst gewijzigde eerst.</summary>
    Task<IReadOnlyList<OpenCartView>> ListOpenAsync(CancellationToken ct = default);
}

/// <summary>
/// Een open winkelkar zoals de verhuurder hem ziet. <see cref="Email"/> is null zolang de klant de eerste stap van
/// het reserveren niet deed. <see cref="ReminderFailed"/>: de herinneringsmail kon niet vertrekken en wordt niet
/// opnieuw geprobeerd.
/// </summary>
public record OpenCartView(
    string? Email,
    DateRange Period,
    IReadOnlyList<OpenCartLine> Lines,
    decimal Total,
    DateTime UpdatedAtUtc,
    DateTime? ReminderSentAtUtc,
    bool ReminderFailed);

/// <summary>Een lijn met de Nederlandse naam; <see cref="Name"/> is null als het artikel of pakket niet meer bestaat of niet meer op de website staat.</summary>
public record OpenCartLine(string? Name, int Quantity);
