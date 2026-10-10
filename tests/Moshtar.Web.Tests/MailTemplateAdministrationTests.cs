using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Mail;
using Moshtar.Application.Reservations;
using Moshtar.Domain.Catalog;
using Moshtar.Domain.Common;
using Moshtar.Domain.Mail;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

/// <summary>Een beheerder past de mailsjablonen voor klantmails aan (ADR 0003).</summary>
public sealed class MailTemplateAdministrationTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private const MailKind Confirmation = MailKind.ReservationConfirmation;
    private static readonly MailTemplateText Custom = new(
        "Hoera {firstName}, reservatie {reservationNumber} staat vast",
        "Dag {firstName} {lastName},\n\nTot binnenkort!\n\n{reservationDetails}\n\nGroeten van {companyName}, {contactEmail}");

    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Without_customization_every_language_shows_the_standard_text()
    {
        await using var scope = await app.TenantScopeAsync("andere");

        var versions = await Templates(scope).GetAsync(Confirmation);

        Assert.Equal(["nl", "fr", "en"], versions.Select(v => v.Culture));
        Assert.All(versions, v => Assert.False(v.IsCustomized));
        Assert.Equal(MailKinds.ReservationConfirmation.Standard("fr"), versions.Single(v => v.Culture == "fr").Text);
    }

    [Fact]
    public async Task Saved_text_is_shown_with_who_changed_it_last()
    {
        var user = await app.CreateUserAsync("hopsakee", "sjabloon-opslaan@hopsakee.test");
        await using var scope = await app.TenantScopeAsync("hopsakee");

        await Templates(scope).SaveAsync(Confirmation, "en", Custom, user.Id);

        var english = (await Templates(scope).GetAsync(Confirmation)).Single(v => v.Culture == "en");
        Assert.True(english.IsCustomized);
        Assert.Equal(Custom, english.Text);
        Assert.Equal("sjabloon-opslaan@hopsakee.test", english.UpdatedByEmail);
        Assert.NotNull(english.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("Hallo {firstNam}", "{reservationDetails}", "Onbekende plaatshouder {firstNam}.")]
    [InlineData("Je reservatie", "Hallo {firstName}", "{reservationDetails} ontbreekt in de tekst.")]
    public async Task Text_with_a_problem_is_refused_and_nothing_is_saved(string subject, string body, string problem)
    {
        var user = await app.CreateUserAsync("hopsakee", $"sjabloon-fout-{problem.Length}@hopsakee.test");
        await using var scope = await app.TenantScopeAsync("hopsakee");

        var error = await Assert.ThrowsAsync<MailTemplateException>(() =>
            Templates(scope).SaveAsync(Confirmation, "fr", new MailTemplateText(subject, body), user.Id));

        Assert.Equal([problem], error.Problems);
        Assert.False((await Templates(scope).GetAsync(Confirmation)).Single(v => v.Culture == "fr").IsCustomized);
    }

    [Fact]
    public async Task A_language_the_rental_company_does_not_offer_is_refused()
    {
        var user = await app.CreateUserAsync("hopsakee", "sjabloon-duits@hopsakee.test");
        await using var scope = await app.TenantScopeAsync("hopsakee");

        await Assert.ThrowsAsync<MailTemplateException>(() => Templates(scope).SaveAsync(Confirmation, "de", Custom, user.Id));
    }

    [Fact]
    public async Task Back_to_standard_only_resets_that_language()
    {
        var user = await app.CreateUserAsync("andere", "sjabloon-reset@andere.test");
        await using var scope = await app.TenantScopeAsync("andere");
        await Templates(scope).SaveAsync(Confirmation, "nl", Custom, user.Id);
        await Templates(scope).SaveAsync(Confirmation, "fr", Custom, user.Id);

        await Templates(scope).ResetAsync(Confirmation, "fr");

        var versions = await Templates(scope).GetAsync(Confirmation);
        Assert.True(versions.Single(v => v.Culture == "nl").IsCustomized);
        Assert.False(versions.Single(v => v.Culture == "fr").IsCustomized);
        Assert.Equal(MailKinds.ReservationConfirmation.Standard("fr"), versions.Single(v => v.Culture == "fr").Text);
        await Templates(scope).ResetAsync(Confirmation, "nl");
    }

    [Fact]
    public async Task Saving_the_standard_text_is_no_customization()
    {
        var user = await app.CreateUserAsync("hopsakee", "sjabloon-standaard@hopsakee.test");
        await using var scope = await app.TenantScopeAsync("hopsakee");

        await Templates(scope).SaveAsync(Confirmation, "nl", MailKinds.ReservationConfirmation.Standard("nl"), user.Id);

        Assert.False((await Templates(scope).GetAsync(Confirmation)).Single(v => v.Culture == "nl").IsCustomized);
    }

    [Fact]
    public async Task Customer_gets_the_customized_text_in_their_language_and_the_standard_text_in_other_languages()
    {
        var user = await app.CreateUserAsync("hopsakee", "sjabloon-klant@hopsakee.test");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Templates(scope).SaveAsync(Confirmation, "en", Custom, user.Id);
        var item = await CreateItemAsync("hopsakee", "sjabloon-artikel", "Springkasteel Draak");

        var english = await ReserveAsync("hopsakee", "engels@example.test", "en", item);
        await ReserveAsync("hopsakee", "frans@example.test", "fr", item);

        var mail = Assert.Single(Outbox.Sent, m => m.To == "engels@example.test");
        Assert.Equal($"Hoera Lotte, reservatie {english.Number} staat vast", mail.Subject);
        Assert.StartsWith("Dag Lotte Peeters,", mail.TextBody);
        Assert.Contains("1 × Springkasteel Draak", mail.TextBody);
        Assert.Contains("Groeten van Hopsakee.fun, info@hopsakee.test", mail.TextBody);
        Assert.Contains("<p>Tot binnenkort!</p>", mail.HtmlBody);
        Assert.StartsWith("Confirmation de votre réservation", Assert.Single(Outbox.Sent, m => m.To == "frans@example.test").Subject);
    }

    [Fact]
    public async Task Customization_of_one_rental_company_never_reaches_customers_of_another()
    {
        var user = await app.CreateUserAsync("hopsakee", "sjabloon-apart@hopsakee.test");
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Templates(scope).SaveAsync(Confirmation, "nl", Custom, user.Id);
        var item = await CreateItemAsync("andere", "sjabloon-andere", "Ballenbad");

        await ReserveAsync("andere", "andere-klant@example.test", "nl", item);

        Assert.StartsWith("Bevestiging van je reservatie", Assert.Single(Outbox.Sent, m => m.To == "andere-klant@example.test").Subject);
        await using (var scope = await app.TenantScopeAsync("hopsakee"))
            await Templates(scope).ResetAsync(Confirmation, "nl");
    }

    [Fact]
    public async Task Administrator_finds_the_mail_templates_in_the_menu()
    {
        var client = await app.LoggedInClientAsync("sjabloon-menu@hopsakee.test", UserRole.Administrator);

        var page = await client.GetAsync("/admin/mailsjablonen");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"admin/mailsjablonen\"", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_has_no_access_to_the_mail_templates()
    {
        var client = await app.LoggedInClientAsync("sjabloon-medewerker@hopsakee.test", UserRole.Staff);

        var response = await client.GetAsync("/admin/mailsjablonen");
        if (response.StatusCode == HttpStatusCode.Redirect)
            response = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("href=\"admin/mailsjablonen\"", await (await client.GetAsync("/admin/reservaties")).Content.ReadAsStringAsync());
    }

    private static IMailTemplateAdministration Templates(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMailTemplateAdministration>();

    private async Task<Reservation> ReserveAsync(string tenant, string email, string culture, Guid item)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        var day = TestReservations.FutureDay(320 + Outbox.Sent.Count);
        var result = await scope.ServiceProvider.GetRequiredService<IReservationService>().ReserveAsync(new ReservationRequest(
            new DateRange(day, day), [new ReservationLineRequest(item, null, 1)],
            new ReservationCustomer("Lotte", "Peeters", email, null, new Address { Street = "Markt 1", PostalCode = "9000", City = "Gent" }),
            DeliveryMethod.Pickup, null, null, culture), ReservationActor.Website);
        return result.Reservation ?? throw new InvalidOperationException("Reservatie geweigerd.");
    }

    private async Task<Guid> CreateItemAsync(string tenant, string slug, string name)
    {
        await using var scope = await app.TenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new RentalItem
        {
            Slug = slug, Stock = 10, Pricing = new Pricing { DayPrice = 100 },
            Translations = new[] { "nl", "fr", "en" }.Select(c => new Translation { Culture = c, Name = name }).ToList(),
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }
}
