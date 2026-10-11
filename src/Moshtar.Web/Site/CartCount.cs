namespace Moshtar.Web.Site;

/// <summary>
/// Het aantal in het winkelkar-icoon, per request. De kop wordt vóór de pagina opgebouwd; reserveert de pagina alles
/// uit de winkelkar, dan zet ze het aantal hier op nul en tekent de kop zich opnieuw, nog voor de HTML vertrekt.
/// </summary>
public sealed class CartCount
{
    public int Value { get; private set; }

    public event Action? Changed;

    private bool _emptied;

    /// <summary>Het aantal uit de winkelkar; telt niet meer zodra de pagina de winkelkar leegmaakte.</summary>
    public void Set(int value)
    {
        if (_emptied) return;
        Value = value;
        Changed?.Invoke();
    }

    /// <summary>Alles uit de winkelkar is gereserveerd.</summary>
    public void Empty()
    {
        _emptied = true;
        Value = 0;
        Changed?.Invoke();
    }
}
