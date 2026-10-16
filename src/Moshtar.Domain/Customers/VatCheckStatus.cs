namespace Moshtar.Domain.Customers;

/// <summary>Wat de btw-databank over het btw-nummer van een klant zei. Een klant zonder btw-nummer heeft geen status.</summary>
public enum VatCheckStatus
{
    /// <summary>Nog niet gecontroleerd: de btw-databank was onbereikbaar, of het nummer kwam binnen zonder controle.</summary>
    NotChecked,

    /// <summary>Geldig: de btw-databank kent het nummer.</summary>
    Valid,

    /// <summary>Ongeldig: een EU-btw-nummer dat de btw-databank na controle niet kent.</summary>
    Invalid,

    /// <summary>Niet te controleren: een btw-nummer van buiten de EU.</summary>
    NotCheckable,
}
