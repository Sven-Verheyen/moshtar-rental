namespace Moshtar.Application.Customers;

/// <summary>Het klantenbestand van de huidige verhuurder als CSV exporteren en importeren; alleen voor de beheerder.</summary>
public interface ICustomerTransfer
{
    /// <summary>Hoeveel rijen een import hoogstens mag hebben.</summary>
    public const int MaxRows = 5000;

    /// <summary>Hoe groot een importbestand hoogstens mag zijn, in bytes.</summary>
    public const int MaxFileSize = 2 * 1024 * 1024;

    /// <summary>Alle klanten behalve geanonimiseerde, op naam gesorteerd, als CSV-tekst (zie <see cref="CustomerCsv"/>).</summary>
    Task<string> ExportAsync(CancellationToken ct = default);

    /// <summary>
    /// Wat een import van deze CSV-tekst zou doen, zonder iets te bewaren: hoeveel klanten erbij komen, en welke rijen
    /// overgeslagen worden en waarom. Overgeslagen wordt een rij met een e-mailadres dat al klant is of hoger in het
    /// bestand al voorkomt, een ontbrekend verplicht veld of een ongeldige waarde (dezelfde regels als manueel toevoegen).
    /// </summary>
    Task<CustomerImportResult> PreviewAsync(string csv, CancellationToken ct = default);

    /// <summary>
    /// Importeert de klanten uit deze CSV-tekst namens deze gebruiker, met dezelfde regels als <see cref="PreviewAsync"/>
    /// op dat moment. Een EU-btw-nummer is daarna nog niet gecontroleerd: de achtergrondcontrole vraagt het de
    /// btw-databank. Alleen een beheerder mag importeren.
    /// </summary>
    Task<CustomerImportResult> ImportAsync(string csv, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// De uitkomst van een voorbeeld of import: hoeveel klanten (zouden) toegevoegd worden en welke rijen niet. Is het hele
/// bestand onbruikbaar (bv. ontbrekende kolommen of te groot), dan staat waarom in <see cref="Errors"/>.
/// </summary>
public sealed record CustomerImportResult(int Count, IReadOnlyList<SkippedCustomer> Skipped, IReadOnlyList<string> Errors)
{
    public static CustomerImportResult Refused(string error) => new(0, [], [error]);
}

/// <summary>Een rij die niet geïmporteerd wordt: het regelnummer in het bestand (de kolomnamen zijn regel 1), naam, e-mail en reden.</summary>
public sealed record SkippedCustomer(int Line, string Name, string Email, string Reason);
