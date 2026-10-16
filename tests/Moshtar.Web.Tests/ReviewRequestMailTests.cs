using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Staat "Reviewvragen sturen" aan, dan krijgt elke klant de dag na de einddag van elke niet-geannuleerde reservatie
/// vanaf 10u Belgische tijd één reviewvraag, tot 3 dagen na de einddag. Het versturen loopt hier met een vaste klok,
/// zonder de achtergrondtaak; de reservaties liggen in de toekomst en de klok wordt ernaartoe gezet.
/// </summary>
public sealed class ReviewRequestMailTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private const string ReviewLink = "https://g.page/r/hopsa-review";

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task From_ten_oclock_the_day_after_the_end_day_the_customer_gets_one_review_request_with_the_button_and_the_qr_code()
    {
        await NewTenantAsync("review");
        var end = TestReservations.FutureDay(1);
        var reservation = await ReserveAsync("review", "Sara@Example.test", "Sara", end);

        Assert.Equal(0, await RunAsync("review", At(end, 23, 0)));
        Assert.Equal(0, await RunAsync("review", At(end.AddDays(1), 9, 59)));
        Assert.Equal(1, await RunAsync("review", At(end.AddDays(1), 10, 0)));
        Assert.Equal(0, await RunAsync("review", At(end.AddDays(1), 10, 15)));
        Assert.Equal(0, await RunAsync("review", At(end.AddDays(2), 10, 0)));

        var mail = Assert.Single(ReviewMails("sara@example.test"));
        Assert.Equal(("noreply@review.test", "review", "info@review.test"), (mail.FromAddress, mail.FromName, mail.ReplyTo));
        Assert.Equal("Hoe was je huur bij review?", mail.Subject);
        Assert.StartsWith("Hallo Sara,", mail.TextBody);
        Assert.Contains($"Schrijf een review: {ReviewLink}", mail.TextBody);
        Assert.Contains($"href=\"{ReviewLink}\"", mail.HtmlBody);
        Assert.Contains(">Schrijf een review</a>", mail.HtmlBody);
        Assert.Contains("<img src=\"https://review.test/review-qr.png\"", mail.HtmlBody);
        Assert.DoesNotContain("{review", mail.TextBody);

        var mailed = Assert.Single(await HistoryAsync("review", reservation), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
        Assert.Equal(At(end.AddDays(1), 10, 0).UtcDateTime, mailed.OccurredAtUtc);
    }

    [Fact]
    public async Task The_review_request_is_in_the_language_of_the_reservation()
    {
        await NewTenantAsync("review-fr");
        var end = TestReservations.FutureDay(2);
        await ReserveAsync("review-fr", "camille@example.test", "Camille", end, culture: "fr");
        await ReserveAsync("review-fr", "alex@example.test", "Alex", end, culture: "en");

        Assert.Equal(2, await RunAsync("review-fr", At(end.AddDays(1), 11, 0)));

        var french = Assert.Single(ReviewMails("camille@example.test"));
        Assert.Equal("Comment s'est passée votre location chez review-fr ?", french.Subject);
        Assert.Contains($"Écrire un avis: {ReviewLink}", french.TextBody);
        var english = Assert.Single(ReviewMails("alex@example.test"));
        Assert.StartsWith("Hello Alex,", english.TextBody);
        Assert.Contains($"Write a review: {ReviewLink}", english.TextBody);
    }

    [Fact]
    public async Task Every_reservation_that_is_not_cancelled_counts_whatever_its_status()
    {
        await NewTenantAsync("review-status");
        var end = TestReservations.FutureDay(3);
        var open = await ReserveAsync("review-status", "niet-afgevinkt@example.test", "Nina", end);
        var completed = await ReserveAsync("review-status", "afgerond@example.test", "Arne", end);
        var cancelled = await ReserveAsync("review-status", "geannuleerd@example.test", "Gert", end);
        await ChangeStatusAsync("review-status", completed, ReservationStatus.Completed);
        await ChangeStatusAsync("review-status", cancelled, ReservationStatus.Cancelled);

        Assert.Equal(2, await RunAsync("review-status", At(end.AddDays(1), 10, 30)));

        Assert.Single(ReviewMails("niet-afgevinkt@example.test"));
        Assert.Single(ReviewMails("afgerond@example.test"));
        Assert.Empty(ReviewMails("geannuleerd@example.test"));
        Assert.DoesNotContain(await HistoryAsync("review-status", cancelled), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
        Assert.Contains(await HistoryAsync("review-status", open), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
    }

    [Fact]
    public async Task Nothing_with_the_switch_off_and_nothing_for_end_days_before_it_was_switched_on()
    {
        var end = TestReservations.FutureDay(4);
        await NewTenantAsync("review-uit", switchedOn: false);
        await ReserveAsync("review-uit", "uit@example.test", "Uma", end);
        // Aangezet de dag na de einddag: die reservatie eindigde ervoor.
        await NewTenantAsync("review-later", since: At(end.AddDays(1), 8, 0));
        await ReserveAsync("review-later", "ervoor@example.test", "Erik", end);
        await ReserveAsync("review-later", "erna@example.test", "Ella", end.AddDays(1));

        Assert.Equal(0, await RunAsync("review-uit", At(end.AddDays(1), 12, 0)));
        Assert.Equal(0, await RunAsync("review-later", At(end.AddDays(1), 12, 0)));
        Assert.Equal(1, await RunAsync("review-later", At(end.AddDays(2), 12, 0)));

        Assert.Empty(ReviewMails("uit@example.test"));
        Assert.Empty(ReviewMails("ervoor@example.test"));
        Assert.Single(ReviewMails("erna@example.test"));
    }

    [Fact]
    public async Task A_request_that_could_not_leave_is_tried_again_up_to_three_days_after_the_end_day()
    {
        await NewTenantAsync("review-laat");
        var end = TestReservations.FutureDay(5);
        await ReserveAsync("review-laat", "derde-dag@example.test", "Dirk", end);
        await ReserveAsync("review-laat", "vierde-dag@example.test", "Vera", end.AddDays(-1));

        // De achtergrondtaak lag stil tot de derde dag na de einddag van de eerste reservatie.
        Assert.Equal(1, await RunAsync("review-laat", At(end.AddDays(3), 23, 45)));

        Assert.Single(ReviewMails("derde-dag@example.test"));
        Assert.Empty(ReviewMails("vierde-dag@example.test"));
    }

    [Fact]
    public async Task Two_reservations_of_the_same_customer_on_the_same_end_day_give_one_mail_and_a_later_reservation_asks_again()
    {
        await NewTenantAsync("review-dubbel");
        var end = TestReservations.FutureDay(6);
        var first = await ReserveAsync("review-dubbel", "Jan@example.test", "Jan", end);
        var second = await ReserveAsync("review-dubbel", "jan@example.test", "Jan", end);
        var later = await ReserveAsync("review-dubbel", "jan@example.test", "Jan", end.AddDays(10));

        Assert.Equal(1, await RunAsync("review-dubbel", At(end.AddDays(1), 10, 0)));
        Assert.Single(ReviewMails("jan@example.test"));
        Assert.Contains(await HistoryAsync("review-dubbel", first), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
        Assert.Contains(await HistoryAsync("review-dubbel", second), e => e.Kind == ReservationEventKind.ReviewRequestMailed);

        Assert.Equal(1, await RunAsync("review-dubbel", At(end.AddDays(11), 10, 0)));
        Assert.Equal(2, ReviewMails("jan@example.test").Count);
        Assert.Single(await HistoryAsync("review-dubbel", later), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
        // Geen herinnering: de volgende dagen vertrekt er niets meer.
        Assert.Equal(0, await RunAsync("review-dubbel", At(end.AddDays(12), 10, 0)));
    }

    [Fact]
    public async Task Two_runs_at_the_same_time_never_send_twice()
    {
        await NewTenantAsync("review-tegelijk");
        var end = TestReservations.FutureDay(7);
        for (var i = 0; i < 5; i++)
            await ReserveAsync("review-tegelijk", $"klant{i}@example.test", "Kim", end);

        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RunAsync("review-tegelijk", At(end.AddDays(1), 10, 0))));

        Assert.Equal(5, runs.Sum());
        for (var i = 0; i < 5; i++)
            Assert.Single(ReviewMails($"klant{i}@example.test"));
    }

    [Fact]
    public async Task The_review_request_text_can_be_changed_per_language_and_keeps_its_link_with_a_preview()
    {
        await NewTenantAsync("review-sjabloon");
        var admin = await app.CreateUserAsync("review-sjabloon", "beheerder@review-sjabloon.test");
        var end = TestReservations.FutureDay(8);
        await ReserveAsync("review-sjabloon", "lien@example.test", "Lien", end);
        await using (var scope = await app.TenantScopeAsync("review-sjabloon"))
        {
            var templates = scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>();
            var standard = MailKinds.ReviewRequest.Standard("nl");

            var error = await Assert.ThrowsAsync<MailTemplateException>(() =>
                templates.SaveAsync(MailKind.ReviewRequest, "nl", standard with { Body = "Hallo {firstName}, laat iets weten." }, admin.Id));
            Assert.Equal(["{reviewLink} ontbreekt in de tekst."], error.Problems);

            var preview = await templates.PreviewAsync(MailKind.ReviewRequest, "nl", standard);
            Assert.Contains($"Schrijf een review: {ReviewLink}", preview.TextBody);
            Assert.Contains("<img src=\"https://review-sjabloon.test/review-qr.png\"", preview.HtmlBody);
            var whatsApp = await templates.PreviewWhatsAppAsync(MailKind.ReviewRequest, "nl", standard);
            Assert.StartsWith("Hallo Lotte,", whatsApp);
            Assert.EndsWith(ReviewLink, whatsApp);

            await templates.SaveAsync(MailKind.ReviewRequest, "nl", standard with { Subject = "Even je mening, {firstName}?", Body = "Dag {firstName}! {reviewLink}" }, admin.Id);
        }

        await RunAsync("review-sjabloon", At(end.AddDays(1), 10, 0));

        var mail = Assert.Single(ReviewMails("lien@example.test", subject: "Even je mening"));
        Assert.Equal("Even je mening, Lien?", mail.Subject);
        Assert.Contains($"Schrijf een review: {ReviewLink}", mail.TextBody);
        Assert.DoesNotContain("review-qr.png", mail.HtmlBody);
    }

    private List<OutgoingMail> ReviewMails(string to, string subject = "huur bij") =>
        Outbox.Sent.Where(m => m.To == to && (m.Subject.Contains(subject) || m.Subject.Contains("location chez") || m.Subject.Contains("rental with"))).ToList();

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => Belgium.At(day, new TimeOnly(hour, minute));

    private async Task<int> RunAsync(string tenant, DateTimeOffset now)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IReviewRequests>().SendDueAsync(now.UtcDateTime);
    }

    private async Task<IReadOnlyList<ReservationHistoryEntry>> HistoryAsync(string tenant, Guid reservationId)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(reservationId);
    }

    private async Task ChangeStatusAsync(string tenant, Guid reservationId, ReservationStatus status)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        await scope.ServiceProvider.GetRequiredService<IReservationService>().ChangeStatusAsync(reservationId, status, ReservationActor.Website);
    }

    /// <summary>Een klant reserveert een springkasteel om af te halen, op één dag.</summary>
    private async Task<Guid> ReserveAsync(string tenant, string email, string firstName, DateOnly day, string culture = "nl")
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
    /// Een verhuurder met een reviewlink en een kasteel met ruime voorraad. "Reviewvragen sturen" staat aan sinds
    /// <paramref name="since"/>, standaard nu (de reservaties liggen in de toekomst), tenzij <paramref name="switchedOn"/> false is.
    /// </summary>
    private async Task NewTenantAsync(string slug, bool switchedOn = true, DateTimeOffset? since = null)
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
