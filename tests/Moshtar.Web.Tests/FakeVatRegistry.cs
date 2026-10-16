using System.Collections.Concurrent;
using Moshtar.Application.Customers;
using Moshtar.Domain.Customers;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een nagebootste btw-databank: VIES is vanuit de tests niet bereikbaar. Antwoordt wat een test voor een nummer
/// instelde, en anders "onbereikbaar". Houdt bij welke nummers opgevraagd werden. Gedeeld door de tests van één
/// klasse: elke test gebruikt zijn eigen nummers.
/// </summary>
public sealed class FakeVatRegistry : IVatRegistry
{
    private readonly ConcurrentDictionary<string, VatRegistryAnswer> _answers = new();
    private readonly ConcurrentQueue<string> _lookups = new();

    public void Answer(string vatNumber, VatRegistryAnswer answer) => _answers[VatNumberFormat.Normalize(vatNumber)!] = answer;

    public void Valid(string vatNumber, string? name = null, string? address = null) =>
        Answer(vatNumber, new VatRegistryAnswer(VatRegistryOutcome.Valid, name, address));

    public int LookupsOf(string vatNumber) => _lookups.Count(n => n == VatNumberFormat.Normalize(vatNumber));

    public Task<VatRegistryAnswer> LookUpAsync(string vatNumber, CancellationToken ct = default)
    {
        var number = VatNumberFormat.Normalize(vatNumber)!;
        if (!VatNumberFormat.IsEu(number))
            throw new ArgumentException("Alleen een EU-btw-nummer kan bij de btw-databank gecontroleerd worden.", nameof(vatNumber));
        _lookups.Enqueue(number);
        return Task.FromResult(_answers.GetValueOrDefault(number, VatRegistryAnswer.Unavailable));
    }
}
