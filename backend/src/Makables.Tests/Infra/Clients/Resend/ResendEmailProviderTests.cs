using System.Net;
using System.Text.Json;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Email;
using Makables.Infra.Clients.Resend;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly.Registry;

namespace Makables.Tests.Infra.Clients.Resend;

/// <summary>
/// Pins the T-0157 <see cref="ResendEmailProvider"/> contract: fully
/// rendered <c>Subject</c>/<c>PlainTextBody</c> ship as
/// <c>POST /emails</c> (no remote template rendering), from-address
/// fallback to options, attachment base64 wiring, and the SendGrid-era
/// failure taxonomy (2xx receipt, 4xx Permanent, 5xx/429 Transient, no
/// response-body leakage into failures).
/// </summary>
public class ResendEmailProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static ResendEmailProvider BuildSut(
        StubHandler handler,
        Action<ResendOptions>? configure = null)
    {
        var options = new ResendOptions { ApiKey = "re_test_key" };
        configure?.Invoke(options);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ResendEmailProvider.HttpClientName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var registry = new ResiliencePipelineRegistry<string>();
        // No retry decorator in tests — the retry strategy itself is pinned
        // by the shared HttpRetryStrategy tests; an empty pipeline passes
        // the call through once.
        registry.TryAddBuilder<HttpResponseMessage>(
            ResendEmailProvider.HttpClientName, (_, _) => { });

        return new ResendEmailProvider(
            factory,
            Options.Create(options),
            registry,
            NullLogger<ResendEmailProvider>.Instance);
    }

    private static EmailMessage Message(Attachment? attachment = null) => new(
        ProviderTemplateId: "d-ignored-by-resend",
        LanguageCode: "cs-CZ",
        ToAddress: "anna@example.cz",
        ToName: "Anna Nováková",
        FromAddress: "objednavky@makables.cz",
        FromName: "Makables objednávky",
        ReplyToAddress: "podpora@makables.cz",
        Subject: "Potvrzení objednávky MK-2026-0001",
        PlainTextBody: "Dobrý den, vaše objednávka byla přijata.",
        Data: new Dictionary<string, object> { ["order_number"] = "MK-2026-0001" })
    {
        Attachment = attachment,
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Sends_rendered_subject_and_text_with_bearer_key()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"re_msg_123"}"""));
        var sut = BuildSut(handler);

        var result = await sut.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().Be("re_msg_123");

        var (request, body) = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://api.resend.com/emails");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("re_test_key");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("from").GetString().Should().Be("Makables objednávky <objednavky@makables.cz>");
        root.GetProperty("to")[0].GetString().Should().Be("anna@example.cz");
        root.GetProperty("subject").GetString().Should().Be("Potvrzení objednávky MK-2026-0001");
        root.GetProperty("text").GetString().Should().Be("Dobrý den, vaše objednávka byla přijata.");
        root.GetProperty("reply_to").GetString().Should().Be("podpora@makables.cz");
        root.TryGetProperty("attachments", out _).Should().BeFalse("no attachment was supplied");
    }

    [Fact]
    public async Task Falls_back_to_options_from_address_when_message_has_none()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"re_1"}"""));
        var sut = BuildSut(handler, o =>
        {
            o.DefaultFromAddress = "no-reply@makables.cz";
            o.DefaultFromName = "Makables";
        });

        var message = Message() with { FromAddress = "", FromName = null };
        var result = await sut.SendAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        json.RootElement.GetProperty("from").GetString()
            .Should().Be("Makables <no-reply@makables.cz>");
    }

    [Fact]
    public async Task Wires_attachment_as_base64_content()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"re_2"}"""));
        var sut = BuildSut(handler);
        var bytes = new byte[] { 1, 2, 3, 4 };

        var result = await sut.SendAsync(
            Message(new Attachment("faktura-FV-CZ-2026-0001.pdf", bytes, "application/pdf")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        var attachment = json.RootElement.GetProperty("attachments")[0];
        attachment.GetProperty("filename").GetString().Should().Be("faktura-FV-CZ-2026-0001.pdf");
        attachment.GetProperty("content").GetString().Should().Be(Convert.ToBase64String(bytes));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Maps_retryable_statuses_to_Transient(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, """{"message":"upstream sad"}"""));
        var sut = BuildSut(handler);

        var result = await sut.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderTransientFailure);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Maps_other_4xx_to_Permanent_without_leaking_the_body(HttpStatusCode status)
    {
        var handler = new StubHandler(_ =>
            Json(status, """{"message":"validation failed for anna@example.cz"}"""));
        var sut = BuildSut(handler);

        var result = await sut.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderPermanentFailure);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        // T-0028 sec reviewer B-1 carried over: the upstream body (which can
        // echo recipient PII) must never ride the failure result.
        result.Error.Details.Should().BeNull();
        result.Error.Code.Should().NotContain("anna@example.cz");
    }

    [Fact]
    public async Task Success_with_unparsable_body_still_returns_a_receipt()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", System.Text.Encoding.UTF8, "text/plain"),
        });
        var sut = BuildSut(handler);

        var result = await sut.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderMessageId.Should().StartWith("resend:unparsed:");
    }

    [Fact]
    public async Task Honours_configured_base_url()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"re_3"}"""));
        var sut = BuildSut(handler, o => o.BaseUrl = "https://resend.test/");

        await sut.SendAsync(Message(), CancellationToken.None);

        handler.Requests.Single().Request.RequestUri!.ToString()
            .Should().Be("https://resend.test/emails");
    }
}
