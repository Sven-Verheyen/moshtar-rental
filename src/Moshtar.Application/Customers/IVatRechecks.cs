namespace Moshtar.Application.Customers;

/// <summary>
/// Controleert op de achtergrond de EU-btw-nummers van de klanten van de huidige verhuurder die nog niet gecontroleerd
/// zijn, omdat de btw-databank onbereikbaar was of omdat ze zonder controle binnenkwamen. Een klant wordt hoogstens één
/// keer per <see cref="RetryAfter"/> opnieuw gevraagd, en per run hoogstens <see cref="BatchSize"/> klanten, die het
/// langst wachten eerst. Een nummer dat ongeldig blijkt, houdt de klant niet tegen: het staat gemarkeerd.
/// </summary>
public interface IVatRechecks
{
    public const int BatchSize = 50;
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>Geeft het aantal klanten waarvoor de btw-databank gevraagd werd.</summary>
    Task<int> RecheckAsync(DateTime nowUtc, CancellationToken ct = default);
}
