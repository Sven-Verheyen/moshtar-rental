using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moshtar.Application.Reviews;
using Moshtar.Domain.Reservations;
using Moshtar.Infrastructure.Mail;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Tenancy;
using static Moshtar.Web.Tests.ReviewTenant;

namespace Moshtar.Web.Tests;

/// <summary>
/// Het versturen van de reviewvragen per verhuurder loopt nooit twee keer tegelijk: een run die er een andere bezig
/// vindt, slaat over. Een reviewvraag telt pas als verstuurd als de mail vertrokken is, en wie zich afmeldt terwijl de
/// run bezig is, krijgt niets meer. De mails gaan hier door een transport waarin de test kan ingrijpen.
/// </summary>
public sealed class ReviewRequestRunTests(MoshtarApp app) : IClassFixture<MoshtarApp>
{
    [Fact]
    public async Task While_one_run_is_sending_another_run_for_the_same_rental_company_sends_nothing()
    {
        await CreateAsync(app, "review-bezig");
        var end = TestReservations.FutureDay(31);
        await ReserveAsync(app, "review-bezig", "eerst@example.test", "Eva", end);
        await ReserveAsync(app, "review-bezig", "daarna@example.test", "Dirk", end);
        var transport = new HookTransport();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnSend = async mail =>
        {
            if (mail.To != "eerst@example.test") return;
            entered.TrySetResult();
            await release.Task;
        };
        using var hooked = With(transport);

        var first = RunAsync(hooked, "review-bezig", At(end.AddDays(1), 10, 0));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = await RunAsync(hooked, "review-bezig", At(end.AddDays(1), 10, 0));
        release.SetResult();

        Assert.Equal(0, second);
        Assert.Equal(2, await first);
        Assert.Single(transport.Sent, m => m.To == "eerst@example.test");
        Assert.Single(transport.Sent, m => m.To == "daarna@example.test");
    }

    [Fact]
    public async Task A_review_request_whose_mail_failed_leaves_in_a_later_run_with_one_history_line()
    {
        await CreateAsync(app, "review-opnieuw");
        var end = TestReservations.FutureDay(32);
        var reservation = await ReserveAsync(app, "review-opnieuw", "opnieuw@example.test", "Olga", end);
        var transport = new HookTransport { OnSend = _ => throw new InvalidOperationException("Geen verbinding") };
        using var hooked = With(transport);

        Assert.Equal(0, await RunAsync(hooked, "review-opnieuw", At(end.AddDays(1), 10, 0)));
        Assert.Empty(transport.Sent);
        Assert.DoesNotContain(await HistoryAsync(app, "review-opnieuw", reservation), e => e.Kind == ReservationEventKind.ReviewRequestMailed);

        transport.OnSend = _ => Task.CompletedTask;
        Assert.Equal(1, await RunAsync(hooked, "review-opnieuw", At(end.AddDays(1), 11, 0)));
        Assert.Equal(0, await RunAsync(hooked, "review-opnieuw", At(end.AddDays(1), 12, 0)));
        Assert.Single(transport.Sent);
        Assert.Single(await HistoryAsync(app, "review-opnieuw", reservation), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
    }

    [Fact]
    public async Task A_customer_who_opts_out_while_the_run_is_sending_gets_no_review_request()
    {
        await CreateAsync(app, "review-net-afgemeld");
        var end = TestReservations.FutureDay(33);
        await ReserveAsync(app, "review-net-afgemeld", "vroeg@example.test", "Vic", end);
        var late = await ReserveAsync(app, "review-net-afgemeld", "Laat@example.test", "Lien", end);
        var transport = new HookTransport();
        using var hooked = With(transport);
        transport.OnSend = async mail =>
        {
            if (mail.To != "vroeg@example.test") return;
            await using var scope = await ScopeAsync(hooked, "review-net-afgemeld");
            await scope.ServiceProvider.GetRequiredService<IReviewOptOuts>().OptOutAsync("laat@example.test");
        };

        Assert.Equal(1, await RunAsync(hooked, "review-net-afgemeld", At(end.AddDays(1), 10, 0)));

        Assert.Single(transport.Sent, m => m.To == "vroeg@example.test");
        Assert.DoesNotContain(transport.Sent, m => m.To.Equals("laat@example.test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(await HistoryAsync(app, "review-net-afgemeld", late), e => e.Kind == ReservationEventKind.ReviewRequestMailed);
    }

    private WebApplicationFactory<Program> With(HookTransport transport) =>
        app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IMailTransport>(transport)));

    private static async Task<int> RunAsync(WebApplicationFactory<Program> at, string tenant, DateTimeOffset now)
    {
        await using var scope = await ScopeAsync(at, tenant);
        return await scope.ServiceProvider.GetRequiredService<IReviewRequests>().SendDueAsync(now.UtcDateTime);
    }

    private static async Task<AsyncServiceScope> ScopeAsync(WebApplicationFactory<Program> at, string tenantSlug)
    {
        var scope = at.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Hosts).SingleAsync(t => t.Slug == tenantSlug);
        scope.ServiceProvider.GetRequiredService<HostTenantContext>().UseTenant(tenant);
        return scope;
    }

    /// <summary>Houdt de vertrokken mails bij; <see cref="OnSend"/> loopt eerst en kan wachten of mislukken.</summary>
    private sealed class HookTransport : IMailTransport
    {
        private readonly ConcurrentQueue<OutgoingMail> _sent = new();

        public Func<OutgoingMail, Task> OnSend { get; set; } = _ => Task.CompletedTask;

        public IReadOnlyCollection<OutgoingMail> Sent => _sent.ToArray();

        public async Task SendAsync(OutgoingMail mail, CancellationToken ct = default)
        {
            await OnSend(mail);
            _sent.Enqueue(mail);
        }
    }
}
