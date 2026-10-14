using System.Globalization;

namespace Moshtar.Infrastructure.Reservations;

/// <summary>Hoe de historiek de meerprijs van een alternatief noteert, in het Nederlands van het back office.</summary>
internal static class SurchargeNote
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-BE");

    /// <returns>"(meerprijs € 25,00)", of "(zonder meerprijs)" zonder meerprijs.</returns>
    public static string For(decimal? surcharge) =>
        surcharge is { } amount && amount > 0 ? $"(meerprijs {amount.ToString("C", Dutch)})" : "(zonder meerprijs)";
}
