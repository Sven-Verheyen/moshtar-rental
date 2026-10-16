namespace Moshtar.Domain.Customers;

/// <summary>
/// Wat de btw-databank over een EU-btw-nummer zegt. <see cref="Name"/> en <see cref="Address"/> zijn er alleen voor een
/// geldig nummer, en niet elk land geeft ze; het adres is één vrije tekst (zie <see cref="VatRegistryAddress"/>).
/// </summary>
public sealed record VatRegistryAnswer(VatRegistryOutcome Outcome, string? Name = null, string? Address = null)
{
    public static readonly VatRegistryAnswer Invalid = new(VatRegistryOutcome.Invalid);
    public static readonly VatRegistryAnswer Unavailable = new(VatRegistryOutcome.Unavailable);
}

public enum VatRegistryOutcome
{
    /// <summary>De btw-databank kent het nummer.</summary>
    Valid,

    /// <summary>De btw-databank kent het nummer niet: het is ongeldig.</summary>
    Invalid,

    /// <summary>De btw-databank (of die van het land) was onbereikbaar of te traag: het nummer is nog niet gecontroleerd.</summary>
    Unavailable,
}
