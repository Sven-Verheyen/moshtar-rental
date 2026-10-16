using System.Net;
using System.Text;
using Moshtar.Domain.Customers;
using Moshtar.Infrastructure.Customers;

namespace Moshtar.Web.Tests;

/// <summary>
/// Hoe Moshtar de antwoorden van de VIES REST API leest. VIES zelf is vanuit de tests niet bereikbaar: de antwoorden
/// hier zijn nagemaakt zoals de API ze geeft.
/// </summary>
public sealed class ViesVatRegistryTests
{
    [Fact]
    public async Task A_known_number_is_valid_with_the_official_name_and_address()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, """
            {"isValid":true,"requestDate":"2026-10-16T20:00:00.000Z","userError":"VALID","name":"BV HOPSAKEE",
             "address":"KERKSTRAAT 1\n2000 ANTWERPEN","requestIdentifier":"","originalVatNumber":"0123456789","vatNumber":"0123456789"}
            """);

        var answer = await Registry(handler).LookUpAsync("BE 0123.456.789");

        Assert.Equal(new VatRegistryAnswer(VatRegistryOutcome.Valid, "BV HOPSAKEE", "KERKSTRAAT 1\n2000 ANTWERPEN"), answer);
        Assert.Equal("https://ec.europa.eu/taxation_customs/vies/rest-api/ms/BE/vat/0123456789", handler.RequestedUri?.ToString());
    }

    [Fact]
    public async Task A_name_and_address_that_the_country_does_not_give_are_left_empty()
    {
        var answer = await Registry(new CannedHandler(HttpStatusCode.OK, """
            {"isValid":true,"userError":"VALID","name":"---","address":"---"}
            """)).LookUpAsync("DE123456789");

        Assert.Equal(new VatRegistryAnswer(VatRegistryOutcome.Valid), answer);
    }

    [Fact]
    public async Task An_unknown_number_is_invalid()
    {
        var answer = await Registry(new CannedHandler(HttpStatusCode.OK, """
            {"isValid":false,"userError":"INVALID","name":"---","address":"---"}
            """)).LookUpAsync("BE0123456789");

        Assert.Equal(VatRegistryAnswer.Invalid, answer);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"isValid":false,"userError":"MS_UNAVAILABLE","name":"---","address":"---"}""")]
    [InlineData(HttpStatusCode.OK, """{"isValid":false,"userError":"TIMEOUT"}""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"actionSucceed":false,"errorWrappers":[{"error":"MS_MAX_CONCURRENT_REQ"}]}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>Service Unavailable</html>")]
    [InlineData(HttpStatusCode.OK, "geen json")]
    public async Task A_registry_that_cannot_answer_is_unavailable_not_invalid(HttpStatusCode status, string body)
    {
        var answer = await Registry(new CannedHandler(status, body)).LookUpAsync("BE0123456789");

        Assert.Equal(VatRegistryAnswer.Unavailable, answer);
    }

    [Fact]
    public async Task A_registry_that_is_too_slow_or_unreachable_is_unavailable()
    {
        var timeout = await Registry(new ThrowingHandler(new TaskCanceledException())).LookUpAsync("BE0123456789");
        var unreachable = await Registry(new ThrowingHandler(new HttpRequestException("proxy"))).LookUpAsync("BE0123456789");

        Assert.Equal((VatRegistryAnswer.Unavailable, VatRegistryAnswer.Unavailable), (timeout, unreachable));
    }

    private static ViesVatRegistry Registry(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(ViesVatRegistry.BaseAddress) });

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw exception;
    }
}
