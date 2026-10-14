using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Application.Schedule;
using Moshtar.Application.Tenancy;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Domain.Rounds;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>
/// Verwachte uren mailen per ronde, met de lokale vervanger van Azure Maps (zie <see cref="RoundTests"/>): het bedrijf
/// ligt in Bergen, de klanten in Gent, Antwerpen en Namen. De mails worden gelogd in plaats van verstuurd.
/// </summary>
public sealed class ExpectedTimeMailTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private static readonly Address Business = new() { Street = "Grote Markt 1", PostalCode = "7000", City = "Bergen" };

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Every_customer_of_the_round_gets_the_mail_in_their_language_with_their_window()
    {
        var day = TestReservations.FutureDay(230);
        var (gent, antwerpen, namen) = await SquareAsync(day, "mail-ieder");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var windows = await WindowsAsync(day, RoundKind.Delivery);

        var sent = await MailAsync(day, RoundKind.Delivery);

        Assert.Equal(3, sent.Count);
        var dutch = Assert.Single(sent, m => m.To == gent.Customer!.Email);
        Assert.StartsWith("Levering van je reservatie bij Hopsakee.fun op ", dutch.Subject);
        Assert.EndsWith($": {windows[gent.Id].ToString("nl")}", dutch.Subject);
        Assert.Contains("Hallo Greet,", dutch.TextBody);
        Assert.Contains(windows[gent.Id].ToString("nl"), dutch.TextBody);
        Assert.Contains(gent.Number, dutch.TextBody);
        var french = Assert.Single(sent, m => m.To == antwerpen.Customer!.Email);
        Assert.Contains(windows[antwerpen.Id].ToString("fr"), french.Subject);
        Assert.StartsWith("Bonjour Anton,", french.TextBody);
        var english = Assert.Single(sent, m => m.To == namen.Customer!.Email);
        Assert.Contains(windows[namen.Id].ToString("en"), english.TextBody);
        Assert.Equal("noreply@hopsakee.test", english.FromAddress);
    }

    [Fact]
    public async Task Mailing_again_only_mails_the_customers_whose_window_changed_since_their_last_mail()
    {
        var day = TestReservations.FutureDay(231);
        await SquareAsync(day, "mail-opnieuw");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        await MailAsync(day, RoundKind.Delivery);

        Assert.Empty(await MailAsync(day, RoundKind.Delivery));

        // Een vast uur op de laatste stop verschuift enkel haar venster.
        var last = (await StopsAsync(day, RoundKind.Delivery)).Last();
        await PlanAsync(p => p.SetFixedTimeAsync(day, RoundKind.Delivery, last.Id, new TimeOnly(15, 0)));
        var windows = await WindowsAsync(day, RoundKind.Delivery);
        var again = await MailAsync(day, RoundKind.Delivery);

        var mail = Assert.Single(again);
        Assert.Equal(last.Customer!.Email, mail.To);
        Assert.Contains("15", windows[last.Id].ToString());
        Assert.Contains(windows[last.Id].ToString(last.Culture), mail.TextBody);
    }

    [Fact]
    public async Task The_collection_round_mails_the_expected_collection_time_on_the_last_day()
    {
        var day = TestReservations.FutureDay(232);
        var (gent, _, _) = await SquareAsync(day, "mail-ophalen");
        var collectionDay = day.AddDays(1);
        await PlanAsync(p => p.ProposeAsync(collectionDay, RoundKind.Collection));
        var windows = await WindowsAsync(collectionDay, RoundKind.Collection);

        var sent = await MailAsync(collectionDay, RoundKind.Collection);

        Assert.Equal(3, sent.Count);
        var dutch = Assert.Single(sent, m => m.To == gent.Customer!.Email);
        Assert.StartsWith("Ophaling van je reservatie bij Hopsakee.fun op ", dutch.Subject);
        Assert.Contains(windows[gent.Id].ToString("nl"), dutch.TextBody);
        Assert.Contains("Veldstraat 10, 9000 Gent", dutch.TextBody);
    }

    [Fact]
    public async Task Each_mail_is_an_event_in_the_history_of_the_reservation_with_who_sent_it()
    {
        var day = TestReservations.FutureDay(233);
        var (gent, _, _) = await SquareAsync(day, "mail-historiek");
        var user = await app.CreateUserAsync("hopsakee", "uren-mailen@hopsakee.test", UserRole.Staff);
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));

        await MailAsync(day, RoundKind.Delivery, ReservationActor.User(user.Id));

        await using var scope = await app.TenantScopeAsync("hopsakee");
        var history = await scope.ServiceProvider.GetRequiredService<IReservationService>().GetHistoryAsync(gent.Id);
        var mailed = Assert.Single(history, e => e.Kind == ReservationEventKind.ExpectedTimeMailed);
        Assert.Equal("uren-mailen@hopsakee.test", mailed.UserEmail);
    }

    [Fact]
    public async Task Without_a_route_nothing_is_mailed()
    {
        var day = TestReservations.FutureDay(234);
        await SquareAsync(day, "mail-geen-route");

        var before = Outbox.Sent.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => MailAsync(day, RoundKind.Delivery));
        Assert.Equal(before, Outbox.Sent.Count);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Administrator)]
    public async Task The_planning_shows_the_mail_button_only_for_a_round_with_a_route(UserRole role)
    {
        var day = TestReservations.FutureDay(role == UserRole.Staff ? 235 : 236);
        await SquareAsync(day, $"mail-knop-{role}");
        var client = await app.LoggedInClientAsync($"uren-knop-{role}@hopsakee.test".ToLowerInvariant(), role);
        Assert.DoesNotContain("Verwachte uren mailen", Section(await PlanningAsync(client, day), "leverronde"));

        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));

        Assert.Contains("data-action=\"uren-mailen\"", Section(await PlanningAsync(client, day), "leverronde"));
        Assert.DoesNotContain("Verwachte uren mailen", Section(await PlanningAsync(client, day), "ophaalronde"));
    }

    [Fact]
    public async Task Each_stop_shows_when_it_was_mailed_and_which_window()
    {
        var day = TestReservations.FutureDay(237);
        await SquareAsync(day, "mail-getoond");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("uren-getoond@hopsakee.test", UserRole.Staff);
        var stops = await StopsAsync(day, RoundKind.Delivery);
        Assert.DoesNotContain("Gemaild op", Section(await PlanningAsync(client, day), "leverronde"));

        await MailAsync(day, RoundKind.Delivery);
        var windows = await WindowsAsync(day, RoundKind.Delivery);
        var last = stops.Last();
        await PlanAsync(p => p.SetFixedTimeAsync(day, RoundKind.Delivery, last.Id, new TimeOnly(15, 0)));
        var round = Section(await PlanningAsync(client, day), "leverronde");

        var first = Stop(round, stops[0].Number);
        Assert.Contains("Gemaild op", first);
        Assert.Contains($"data-mailed-window=\"{windows[stops[0].Id]}\"", first);
        Assert.DoesNotContain("Venster gewijzigd", first);
        var changed = Stop(round, last.Number);
        Assert.Contains($"data-mailed-window=\"{windows[last.Id]}\"", changed);
        Assert.Contains("Venster gewijzigd sinds de mail", changed);
    }

    [Fact]
    public async Task Each_stop_of_a_route_has_a_whatsapp_link_to_the_customer_with_the_message_in_their_language()
    {
        var day = TestReservations.FutureDay(238);
        var (_, antwerpen, _) = await SquareAsync(day, "whatsapp-link");
        var client = await app.LoggedInClientAsync("whatsapp-link@hopsakee.test", UserRole.Staff);
        Assert.DoesNotContain("wa.me", Section(await PlanningAsync(client, day), "leverronde"));

        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));

        var stop = (await ScheduleAsync(day)).Deliveries.Single(s => s.ReservationId == antwerpen.Id).InRound!;
        Assert.StartsWith("https://wa.me/32470123456?text=Bonjour%20Anton%2C%20", stop.WhatsAppLink);
        var link = stop.WhatsAppLink!;
        var message = Uri.UnescapeDataString(link[(link.IndexOf("text=") + 5)..]);
        Assert.Contains($"{antwerpen.Number} chez Hopsakee.fun", message);
        Assert.Contains(stop.Window.ToString("fr"), message);
        Assert.DoesNotContain("{", message);
        var html = Stop(Section(await PlanningAsync(client, day), "leverronde"), antwerpen.Number);
        Assert.Contains($"href=\"{stop.WhatsAppLink}\"", html);
        Assert.Contains("target=\"_blank\"", html[html.IndexOf("wa.me")..]);
    }

    [Fact]
    public async Task The_whatsapp_message_follows_the_customized_template_of_the_collection()
    {
        var day = TestReservations.FutureDay(239);
        var (gent, _, _) = await SquareAsync(day, "whatsapp-sjabloon");
        var user = await app.CreateUserAsync("hopsakee", "whatsapp-sjabloon@hopsakee.test");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>().SaveAsync(MailKind.ExpectedCollectionTime, "nl",
                MailKinds.ExpectedCollectionTime.Standard("nl") with { WhatsApp = "Dag {firstName}, we komen {expectedWindow}." }, user.Id);
        var collectionDay = day.AddDays(1);

        await PlanAsync(p => p.ProposeAsync(collectionDay, RoundKind.Collection));

        var stop = (await ScheduleAsync(collectionDay)).Collections.Single(s => s.ReservationId == gent.Id).InRound!;
        Assert.Equal($"https://wa.me/32470123456?text=Dag%20Greet%2C%20we%20komen%20{Uri.EscapeDataString(stop.Window.ToString("nl"))}.", stop.WhatsAppLink);
    }

    [Fact]
    public async Task A_customer_without_a_phone_number_gets_no_whatsapp_button()
    {
        var day = TestReservations.FutureDay(240);
        var (gent, _, namen) = await SquareAsync(day, "whatsapp-geen-nummer");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
        {
            // Een reservatie van voor het telefoonnummer verplicht was.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = await db.Reservations.Where(r => r.Id == gent.Id).Select(r => r.Customer!).SingleAsync();
            customer.Phone = null;
            await db.SaveChangesAsync();
        }
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("whatsapp-geen-nummer@hopsakee.test", UserRole.Staff);

        var deliveries = (await ScheduleAsync(day)).Deliveries;
        Assert.Null(deliveries.Single(s => s.ReservationId == gent.Id).InRound!.WhatsAppLink);
        Assert.NotNull(deliveries.Single(s => s.ReservationId == namen.Id).InRound!.WhatsAppLink);
        var round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.DoesNotContain("wa.me", Stop(round, gent.Number));
        Assert.Contains("wa.me", Stop(round, namen.Number));
    }

    [Fact]
    public async Task Pressing_the_whatsapp_button_is_remembered_and_shown_per_stop()
    {
        var day = TestReservations.FutureDay(241);
        var (gent, antwerpen, _) = await SquareAsync(day, "whatsapp-klik");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        var client = await app.LoggedInClientAsync("whatsapp-klik@hopsakee.test", UserRole.Staff);
        Assert.DoesNotContain("WhatsApp op", Section(await PlanningAsync(client, day), "leverronde"));

        var before = DateTimeOffset.UtcNow;
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IRoundMessages>().RecordWhatsAppAsync(day, RoundKind.Delivery, gent.Id);

        var deliveries = (await ScheduleAsync(day)).Deliveries;
        Assert.InRange(deliveries.Single(s => s.ReservationId == gent.Id).InRound!.WhatsAppAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.Null(deliveries.Single(s => s.ReservationId == antwerpen.Id).InRound!.WhatsAppAt);
        var round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("WhatsApp op", Stop(round, gent.Number));
        Assert.DoesNotContain("WhatsApp op", Stop(round, antwerpen.Number));
    }

    [Fact]
    public async Task Without_a_route_no_whatsapp_click_is_recorded()
    {
        var day = TestReservations.FutureDay(242);
        var (gent, _, _) = await SquareAsync(day, "whatsapp-geen-route");

        await using var scope = await app.TenantScopeAsync("hopsakee");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IRoundMessages>().RecordWhatsAppAsync(day, RoundKind.Delivery, gent.Id));
    }

    [Fact]
    public async Task Only_stops_that_still_belong_to_the_round_are_mailed()
    {
        var day = TestReservations.FutureDay(243);
        var (gent, antwerpen, namen) = await SquareAsync(day, "mail-leden");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        await MoveAsync(gent.Id, TestReservations.FutureDay(330));
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await scope.ServiceProvider.GetRequiredService<IReservationService>().CorrectDeliveryAddressAsync(
                antwerpen.Id, new Address { Street = "Meir 99", PostalCode = "2000", City = "Antwerpen" }, ReservationActor.Website);

        var sent = await MailAsync(day, RoundKind.Delivery);

        Assert.Equal([namen.Customer!.Email], sent.Select(m => m.To));
    }

    [Fact]
    public async Task No_whatsapp_click_is_recorded_for_a_stop_that_left_the_round()
    {
        var day = TestReservations.FutureDay(244);
        var (gent, _, _) = await SquareAsync(day, "whatsapp-weg");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        await MoveAsync(gent.Id, TestReservations.FutureDay(330));

        await using var scope = await app.TenantScopeAsync("hopsakee");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IRoundMessages>().RecordWhatsAppAsync(day, RoundKind.Delivery, gent.Id));
    }

    [Fact]
    public async Task A_changed_round_has_no_mail_button_until_it_is_updated()
    {
        var day = TestReservations.FutureDay(246);
        await SquareAsync(day, "mail-gewijzigd");
        await PlanAsync(p => p.ProposeAsync(day, RoundKind.Delivery));
        await ReserveAsync("Bert", "brussel-mail-gewijzigd", day, "Wetstraat 10", "1000", "Brussel", "nl");
        var client = await app.LoggedInClientAsync("uren-gewijzigd@hopsakee.test", UserRole.Staff);

        var round = Section(await PlanningAsync(client, day), "leverronde");
        Assert.Contains("Ronde gewijzigd", round);
        Assert.DoesNotContain("data-action=\"uren-mailen\"", round);
        Assert.Contains("Werk eerst de ronde bij", round);

        await PlanAsync(p => p.UpdateAsync(day, RoundKind.Delivery));
        Assert.Contains("data-action=\"uren-mailen\"", Section(await PlanningAsync(client, day), "leverronde"));
    }

    /// <summary>Verzet een reservatie naar een andere dag, zoals een latere datumwijziging zou doen.</summary>
    private async Task MoveAsync(Guid reservationId, DateOnly start)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reservation = await db.Reservations.SingleAsync(r => r.Id == reservationId);
        (reservation.StartDate, reservation.EndDate) = (start, start.AddDays(1));
        await db.SaveChangesAsync();
    }

    private async Task<ScheduleDay> ScheduleAsync(DateOnly day)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        return await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
    }

    /// <summary>Het bedrijf in Bergen, en een levering in Gent (nl), Antwerpen (fr) en Namen (en) op deze dag.</summary>
    private async Task<(Reservation Gent, Reservation Antwerpen, Reservation Namen)> SquareAsync(DateOnly day, string tag)
    {
        await MoveBusinessAsync(Business);
        return (
            await ReserveAsync("Greet", $"gent-{tag}", day, "Veldstraat 10", "9000", "Gent", "nl"),
            await ReserveAsync("Anton", $"antwerpen-{tag}", day, "Meir 10", "2000", "Antwerpen", "fr"),
            await ReserveAsync("Nico", $"namen-{tag}", day, "Rue de Fer 10", "5000", "Namen", "en"));
    }

    private async Task PlanAsync(Func<IRoundPlanner, Task> plan)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        await plan(scope.ServiceProvider.GetRequiredService<IRoundPlanner>());
    }

    /// <summary>Drukt op "Verwachte uren mailen" en geeft de mails die daardoor vertrokken.</summary>
    private async Task<IReadOnlyList<OutgoingMail>> MailAsync(DateOnly day, RoundKind kind, ReservationActor? actor = null)
    {
        var before = Outbox.Sent.Count;
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var count = await scope.ServiceProvider.GetRequiredService<IRoundMessages>().MailExpectedTimesAsync(day, kind, actor ?? ReservationActor.Website);
        var sent = Outbox.Sent.Skip(before).ToList();
        Assert.Equal(sent.Count, count);
        return sent;
    }

    /// <summary>De reservaties van de ronde, in rijvolgorde.</summary>
    private async Task<List<Reservation>> StopsAsync(DateOnly day, RoundKind kind)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var schedule = await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
        var ids = (kind == RoundKind.Delivery ? schedule.Deliveries : schedule.Collections)
            .Where(s => s.InRound is not null).OrderBy(s => s.InRound!.Position).Select(s => s.ReservationId).ToList();
        var reservations = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reservations.AsNoTracking()
            .Include(r => r.Customer).Where(r => ids.Contains(r.Id)).ToListAsync();
        return ids.Select(id => reservations.Single(r => r.Id == id)).ToList();
    }

    /// <summary>Het verwacht tijdsvenster per reservatie, zoals de Planning het toont.</summary>
    private async Task<Dictionary<Guid, ExpectedWindow>> WindowsAsync(DateOnly day, RoundKind kind)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var schedule = await scope.ServiceProvider.GetRequiredService<ISchedule>().GetDayAsync(day);
        return (kind == RoundKind.Delivery ? schedule.Deliveries : schedule.Collections)
            .Where(s => s.InRound is not null)
            .ToDictionary(s => s.ReservationId, s => s.InRound!.Window);
    }

    private static async Task<string> PlanningAsync(HttpClient client, DateOnly day)
    {
        var response = await client.GetAsync($"/admin/planning?dag={day:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Het stuk van de pagina tussen het begin van de sectie met deze id en de volgende sectie.</summary>
    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Sectie {id} niet gevonden.");
        var end = html.IndexOf("<section", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    /// <summary>Het kaartje van één reservatie binnen een sectie, tot het volgende kaartje.</summary>
    private static string Stop(string section, string number)
    {
        var start = section.IndexOf($"data-reservation=\"{number}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Reservatie {number} niet gevonden.");
        var end = section.IndexOf("data-reservation=", start + 1, StringComparison.Ordinal);
        return end < 0 ? section[start..] : section[start..end];
    }

    private async Task MoveBusinessAsync(Address address)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var details = scope.ServiceProvider.GetRequiredService<IBusinessDetailsAdministration>();
        await details.SaveAsync(await details.GetAsync() with { Street = address.Street, PostalCode = address.PostalCode, City = address.City });
    }

    private async Task<Reservation> ReserveAsync(
        string firstName, string lastName, DateOnly day, string street, string postalCode, string city, string culture)
    {
        await using var scope = await app.TenantScopeAsync("hopsakee");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.RentalItems.SingleOrDefaultAsync(i => i.Slug == "springkasteel");
        if (item is null)
        {
            item = new RentalItem { Slug = "springkasteel", Stock = 50, Pricing = new Pricing { DayPrice = 100 }, Translations = [new Translation { Culture = "nl", Name = "Springkasteel" }] };
            db.RentalItems.Add(item);
            await db.SaveChangesAsync();
        }
        var address = new Address { Street = street, PostalCode = postalCode, City = city };
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day.AddDays(1)),
            [new ReservationLineRequest(item.Id, null, 1)],
            new ReservationCustomer(firstName, lastName, $"{firstName}.{lastName}@example.test".ToLowerInvariant(), "0470 12 34 56", address),
            DeliveryMethod.Delivery, address, null, culture), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reserveren mislukt.");
    }
}
