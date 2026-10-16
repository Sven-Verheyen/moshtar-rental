using Moshtar.Domain.Customers;

namespace Moshtar.Application.Customers;

/// <summary>
/// De btw-databank van de EU (VIES). Alleen voor een EU-btw-nummer (<see cref="VatNumberFormat.IsEu"/>). Gooit geen
/// fout als de databank onbereikbaar, traag of in de war is: dan is het antwoord <see cref="VatRegistryOutcome.Unavailable"/>,
/// wat iets anders is dan ongeldig.
/// </summary>
public interface IVatRegistry
{
    Task<VatRegistryAnswer> LookUpAsync(string vatNumber, CancellationToken ct = default);
}
