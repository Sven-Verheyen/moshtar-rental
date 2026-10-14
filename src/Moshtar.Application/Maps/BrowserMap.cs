namespace Moshtar.Application.Maps;

/// <summary>
/// Wat de browser nodig heeft om de kaart te tonen (ADR 0006): enkel de sleutel die kaarttegels mag tonen, nooit die
/// van de server, want wat de browser krijgt, kan iedereen lezen. <see cref="Key"/> null: geen kaart.
/// </summary>
public sealed record BrowserMap(string? Key);
