using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reservations;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Een verhuurder die reviewvragen stuurt, voor de tests van de reviewvragen: reservaties in de toekomst, een vaste
/// klok die ernaartoe gezet wordt en de opgevangen mails.
/// </summary>
internal static class ReviewTenant
{
    public const string ReviewLink = "https://g.page/r/hopsa-review";

    /// <summary>De reviewvragen aan dit adres, in elke taal.</summary>
    public static List<OutgoingMail> Mails(MoshtarApp app, string to, string subject = "huur bij") =>
        app.Services.GetRequiredService<RecordingMailTransport>().Sent
            .Where(m => m.To == to && (m.Subject.Contains(subject) || m.Subject.Contains("location chez") || m.Subject.Contains("rental with"))).ToList();

    public static DateTimeOffset At(DateOnly day, int hour, int minute) => Belgium.At(day, new TimeOnly(hour, minute));

    public static async Task<int> RunAsync(MoshtarApp app, string tenant, DateTimeOffset now)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IReviewRequests>().SendDueAsync(now.UtcDateTime);
    }

    public static async Task<IReadOnlyList<ReservationHistoryEntry>> HistoryAsync(MoshtarApp app, string tenant, Guid reservationId)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(reservationId);
    }

    /// <summary>Een klant reserveert een springkasteel om af te halen, op één dag.</summary>
    public static async Task<Guid> ReserveAsync(MoshtarApp app, string tenant, string email, string firstName, DateOnly day, string culture = "nl")
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        var item = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RentalItems.SingleAsync(i => i.Slug == "kasteel");
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day),
            [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, "Klant", email, "0470 12 34 56", new Address { Street = "Kerkstraat 1", PostalCode = "2000", City = "Antwerpen" }),
            DeliveryMethod.Pickup, null, null, culture), ReservationActor.Website);
        return result.Reservation?.Id ?? throw new InvalidOperationException("Reserveren mislukt: " + string.Join(", ", result.Shortages));
    }

    /// <summary>
    /// Een verhuurder op het domein "{slug}.test" met een reviewlink en een kasteel met ruime voorraad. "Reviewvragen
    /// sturen" staat aan sinds <paramref name="since"/>, standaard nu (de reservaties liggen in de toekomst), tenzij
    /// <paramref name="switchedOn"/> false is.
    /// </summary>
    public static async Task CreateAsync(MoshtarApp app, string slug, bool switchedOn = true, DateTimeOffset? since = null)
    {
        await app.CreateTenantAsync(slug, $"{slug}.test");
        await using var scope = await app.TenantScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == slug);
        tenant.ReviewLink = ReviewLink;
        tenant.ReviewRequestsSince = switchedOn ? (since ?? DateTimeOffset.UtcNow).ToUniversalTime() : null;
        db.RentalItems.Add(new RentalItem
        {
            Slug = "kasteel", Stock = 20, Pricing = new Pricing { DayPrice = 100 },
            Translations = [new Translation { Culture = "nl", Name = "Kasteel" }],
        });
        await db.SaveChangesAsync();
    }
}
