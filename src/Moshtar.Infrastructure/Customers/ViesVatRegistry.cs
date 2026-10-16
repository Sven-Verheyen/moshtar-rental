using System.Text.Json;
using System.Text.Json.Serialization;
using Moshtar.Application.Customers;
using Moshtar.Domain.Customers;

namespace Moshtar.Infrastructure.Customers;

/// <summary>
/// De btw-databank via de VIES REST API van de Europese Commissie: gratis, zonder sleutel, met een korte timeout op de
/// HttpClient omdat een klant op het antwoord wacht. Alleen "INVALID" maakt een nummer ongeldig; elke andere fout
/// (MS_UNAVAILABLE, TIMEOUT, SERVICE_UNAVAILABLE, MS_MAX_CONCURRENT_REQ, een HTTP-fout, een timeout) betekent
/// onbereikbaar. Niet bereikbaar vanuit de tests: die gebruiken een nagebootste databank.
/// </summary>
public sealed class ViesVatRegistry(HttpClient http) : IVatRegistry
{
    public const string BaseAddress = "https://ec.europa.eu/taxation_customs/vies/rest-api/";

    /// <summary>Wat VIES teruggeeft voor een naam of adres dat het land niet meedeelt.</summary>
    private const string NotProvided = "---";

    public async Task<VatRegistryAnswer> LookUpAsync(string vatNumber, CancellationToken ct = default)
    {
        if (VatNumberFormat.Normalize(vatNumber) is not { } number || !VatNumberFormat.IsEu(number))
            throw new ArgumentException("Alleen een EU-btw-nummer kan bij de btw-databank gecontroleerd worden.", nameof(vatNumber));
        try
        {
            using var response = await http.GetAsync($"ms/{number[..2]}/vat/{Uri.EscapeDataString(number[2..])}", ct);
            if (!response.IsSuccessStatusCode) return VatRegistryAnswer.Unavailable;
            var result = await JsonSerializer.DeserializeAsync<ViesResult>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return result switch
            {
                { IsValid: true } => new VatRegistryAnswer(VatRegistryOutcome.Valid, Provided(result.Name), Provided(result.Address)),
                { UserError: "INVALID" } => VatRegistryAnswer.Invalid,
                _ => VatRegistryAnswer.Unavailable,
            };
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return VatRegistryAnswer.Unavailable;
        }
    }

    private static string? Provided(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim() == NotProvided ? null : text.Trim();

    private sealed record ViesResult(
        [property: JsonPropertyName("isValid")] bool IsValid,
        [property: JsonPropertyName("userError")] string? UserError,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address")] string? Address);
}
