using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;

namespace Moshtar.Domain.Reviews;

/// <summary>
/// Wie een reviewvraag krijgt, per mail en via de WhatsApp-knop in de Planning: elke niet-geannuleerde reservatie met
/// een klant, van 1 tot 3 dagen na haar einddag, als die einddag op of na het aanzetten van "Reviewvragen sturen" valt.
/// Reservaties van dezelfde klant (e-mailadres) met dezelfde einddag krijgen samen één reviewvraag.
/// </summary>
public static class ReviewRequestRule
{
    /// <summary>Het uur waarop de reviewvraag de dag na de einddag vertrekt, Belgische tijd.</summary>
    public static readonly TimeOnly MailFrom = new(10, 0);

    /// <summary>Hoeveel dagen na de einddag een reviewvraag nog mag; later is te laat.</summary>
    public const int LastDayAfterEnd = 3;

    /// <summary>
    /// De einddagen die op deze dag aan de beurt zijn: 1 tot <see cref="LastDayAfterEnd"/> dagen ervoor, niet vóór de
    /// dag waarop de schakelaar aangezet werd (<paramref name="since"/>). Null als er geen zijn.
    /// </summary>
    public static DateRange? EndDays(DateOnly day, DateTimeOffset since) => Range(day.AddDays(-LastDayAfterEnd), day.AddDays(-1), since);

    /// <summary>
    /// De einddagen waarvoor op dit moment een mail mag vertrekken: zoals <see cref="EndDays"/> voor vandaag, maar wie
    /// gisteren eindigde pas vanaf <see cref="MailFrom"/>.
    /// </summary>
    public static DateRange? EndDaysForMail(DateTimeOffset now, DateTimeOffset since)
    {
        var local = Belgium.Local(now);
        var today = DateOnly.FromDateTime(local.DateTime);
        var last = today.AddDays(TimeOnly.FromDateTime(local.DateTime) >= MailFrom ? -1 : -2);
        return Range(today.AddDays(-LastDayAfterEnd), last, since);
    }

    /// <summary>
    /// De reservaties die samen één reviewvraag krijgen, per klant (e-mailadres zoals <see cref="ReviewOptOut.Normalize"/>
    /// het schrijft) en einddag, in de volgorde van <paramref name="reservations"/>. Zonder geannuleerde reservaties en
    /// reservaties zonder klant.
    /// </summary>
    public static IEnumerable<IGrouping<(string Email, DateOnly EndDate), Reservation>> PerCustomer(IEnumerable<Reservation> reservations) =>
        reservations
            .Where(r => r.Status != ReservationStatus.Cancelled && r.Customer is not null)
            .GroupBy(r => (ReviewOptOut.Normalize(r.Customer!.Email), r.EndDate));

    private static DateRange? Range(DateOnly first, DateOnly last, DateTimeOffset since)
    {
        // Alleen einddagen op of na de dag van het aanzetten: oude klanten krijgen niets.
        var switchedOn = DateOnly.FromDateTime(Belgium.Local(since).DateTime);
        if (switchedOn > first) first = switchedOn;
        return first <= last ? new DateRange(first, last) : null;
    }
}
