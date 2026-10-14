using Moshtar.Domain.Availability;

namespace Moshtar.Application.Catalog;

/// <summary>
/// Wat de beheerder van een blokkering ingeeft: hoeveel exemplaren van welk artikel vanaf wanneer niet verhuurbaar zijn,
/// en tot wanneer. Zonder einddatum zijn alle dagen vanaf de begindatum geblokkeerd, tot de beheerder er een invult.
/// </summary>
public sealed record BlockoutInput(Guid RentalItemId, int Quantity, DateOnly StartDate, DateOnly? EndDate, string? Reason);

/// <summary>Een blokkering in het back office, met de naam van het artikel in de standaardtaal.</summary>
public sealed record BlockoutSummary(Guid Id, string RentalItemName, BlockoutInput Input);

/// <summary>Een bewaarde blokkering en de tekorten van haar artikel vanaf vandaag die het bewaren veroorzaakte of erger maakte.</summary>
public sealed record BlockoutSaved(Guid Id, IReadOnlyList<Shortage> Shortages);

/// <summary>Een blokkering kan zo niet bewaard worden; de boodschappen zijn voor de beheerder bedoeld.</summary>
public sealed class BlockoutException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// De blokkeringen van de verhuurder beheren, in het back office (alleen de Beheerder): aanmaken, wijzigen en
/// verwijderen. Een blokkering mag meer bezetten dan nog vrij is: na het bewaren krijgt de beheerder de tekorten die
/// het artikel dan heeft, zoals bij een lagere voorraad (<see cref="IRentalItemAdministration.SaveAsync"/>).
/// </summary>
public interface IBlockoutAdministration
{
    /// <summary>
    /// De blokkeringen die vandaag of later nog lopen, van één artikel of (zonder artikel) van alle artikelen, op begindatum.
    /// </summary>
    Task<IReadOnlyList<BlockoutSummary>> GetCurrentAsync(Guid? rentalItemId = null, CancellationToken ct = default);

    /// <summary>Gooit een <see cref="BlockoutException"/> als de invoer niet klopt, en bewaart dan niets.</summary>
    Task<BlockoutSaved> CreateAsync(BlockoutInput input, CancellationToken ct = default);

    /// <summary>Gooit een <see cref="BlockoutException"/> als de invoer niet klopt, en bewaart dan niets.</summary>
    Task<BlockoutSaved> SaveAsync(Guid blockoutId, BlockoutInput input, CancellationToken ct = default);

    /// <summary>Verwijdert een blokkering: de exemplaren zijn weer verhuurbaar.</summary>
    Task DeleteAsync(Guid blockoutId, CancellationToken ct = default);
}
