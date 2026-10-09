using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Mail;
using Moshtar.Domain.Tenants;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;

namespace Moshtar.Web.Tests;

public sealed class MailTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    private RecordingMailTransport Outbox => app.Services.GetRequiredService<RecordingMailTransport>();

    [Fact]
    public async Task Mail_leaves_from_the_domain_of_the_rental_company_with_its_contact_address_as_reply_to()
    {
        await SendAsync("hopsakee", "klant1@example.test");

        var mail = Assert.Single(Outbox.Sent, m => m.To == "klant1@example.test");
        Assert.Equal("noreply@hopsakee.test", mail.FromAddress);
        Assert.Equal("Hopsakee.fun", mail.FromName);
        Assert.Equal("info@hopsakee.test", mail.ReplyTo);
        Assert.Equal("Je reservatie", mail.Subject);
    }

    [Fact]
    public async Task Mail_of_one_rental_company_never_uses_the_sender_of_another()
    {
        await SendAsync("hopsakee", "klant2@example.test");
        await SendAsync("andere", "klant3@example.test");

        Assert.Equal("noreply@hopsakee.test", Assert.Single(Outbox.Sent, m => m.To == "klant2@example.test").FromAddress);
        var andere = Assert.Single(Outbox.Sent, m => m.To == "klant3@example.test");
        Assert.Equal("noreply@andere.test", andere.FromAddress);
        Assert.Equal("contact@andere.test", andere.ReplyTo);
    }

    [Fact]
    public async Task Rental_company_without_a_sender_address_cannot_send_mail()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant { Name = "Zonder afzender", Slug = "zonder-afzender" });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync("zonder-afzender", "klant4@example.test"));

        Assert.Contains("afzenderadres", error.Message);
        Assert.DoesNotContain(Outbox.Sent, m => m.To == "klant4@example.test");
    }

    [Fact]
    public void In_production_a_connection_string_sends_mail_through_Azure_Communication_Services()
    {
        using var production = WithAzureConnectionString("Production");

        Assert.Equal("AzureMailTransport", production.Services.GetRequiredService<IMailTransport>().GetType().Name);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Development_and_tests_never_send_real_mail_even_with_a_connection_string(string environment)
    {
        using var local = WithAzureConnectionString(environment);

        Assert.IsType<RecordingMailTransport>(local.Services.GetRequiredService<IMailTransport>());
    }

    [Fact]
    public async Task Rental_company_without_a_contact_address_cannot_send_mail()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.Add(new Tenant { Name = "Zonder contact", Slug = "zonder-contact", SenderEmail = "noreply@zonder-contact.test" });
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync("zonder-contact", "klant5@example.test"));

        Assert.Contains("contactadres", error.Message);
        Assert.DoesNotContain(Outbox.Sent, m => m.To == "klant5@example.test");
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithAzureConnectionString(string environment) =>
        app.WithWebHostBuilder(b => b
            .UseEnvironment(environment)
            .UseSetting("Mail:AzureCommunicationServicesConnectionString",
                "endpoint=https://moshtar-test.communication.azure.com/;accesskey=" + Convert.ToBase64String(new byte[32])));

    private async Task SendAsync(string tenantSlug, string to)
    {
        await using var scope = await app.TenantScopeAsync(tenantSlug);
        await scope.ServiceProvider.GetRequiredService<IMailer>()
            .SendAsync(new MailMessage(to, "Je reservatie", "<p>Bevestigd</p>", "Bevestigd"));
    }
}
