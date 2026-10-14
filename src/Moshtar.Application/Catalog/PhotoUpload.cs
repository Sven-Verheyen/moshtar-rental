namespace Moshtar.Application.Catalog;

/// <summary>Een foto die de beheerder uploadt bij een artikel of pakket: de bestandsnaam (voor een melding) en de inhoud.</summary>
public sealed record PhotoUpload(string FileName, Stream Content);
