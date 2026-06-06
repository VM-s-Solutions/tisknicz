using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Comgate;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.Comgate;

/// <summary>
/// Pins the T-0066 <see cref="ComgatePaymentProvider.ParseAndVerifyWebhookAsync"/>
/// contract. The adapter parses a form-urlencoded request body, calls
/// <see cref="ComgatePaymentProvider.VerifyPaymentAsync"/> to re-fetch
/// authoritative status, and returns a <see cref="WebhookPayload"/>
/// containing the re-fetched state — never the body's own status field.
///
/// <para>
/// Wire shape: a <see cref="DefaultHttpContext"/> holds a form body the
/// adapter reads; the stub HTTP handler scripts the re-fetch response.
/// The malformed-body branches surface as
/// <see cref="BusinessErrorMessage.PaymentWebhookMalformed"/>; the
/// re-fetch failures propagate verbatim from the underlying
/// VerifyPaymentAsync.
/// </para>
/// </summary>
public class ComgatePaymentProviderWebhookTests
{
    private const string TestMerchant = "12345";
    private const string TestSecret = "super-secret-do-not-log";
    private const string TestBaseUrl = "https://payments.comgate.test";
    private const string TestTransId = "AB1C-D34E";
    private const string TestRefId = "ord-1";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly InMemoryLoggerProvider _loggerProvider = new();
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private ComgatePaymentProvider _sut = default!;

    public ComgatePaymentProviderWebhookTests()
    {
        BuildSut(testMode: false);
    }

    private void BuildSut(bool testMode)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ComgatePaymentProvider.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new ComgateOptions
        {
            MerchantId = TestMerchant,
            Secret = TestSecret,
            BaseUrl = TestBaseUrl,
            TestMode = testMode,
        });

        var registry = new Polly.Registry.ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            ComgatePaymentProvider.HttpClientName,
            (builder, _) => { /* no retry */ });

        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(_loggerProvider));
        var logger = loggerFactory.CreateLogger<ComgatePaymentProvider>();

        _sut = new ComgatePaymentProvider(factory, opts, _configs, registry, logger);
    }

    private static HttpRequest BuildFormRequest(IDictionary<string, string> fields)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var form = new FormCollection(
            fields.ToDictionary(
                kv => kv.Key,
                kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));
        context.Request.Form = form;
        return context.Request;
    }

    private static HttpRequest BuildJsonRequest(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    private static HttpResponseMessage FormUrlEncoded(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

    private static IDictionary<string, string> ValidPaidFormBody() => new Dictionary<string, string>
    {
        ["transId"] = TestTransId,
        ["refId"] = TestRefId,
        ["status"] = "PAID",
        ["test"] = "false",
    };

    // ---- Happy path + state mapping ----

    [Fact]
    public async Task Happy_path_form_body_parsed_and_reFetch_returns_Paid()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PAID&method=CARD_CZ&paymentTime=2026-06-06T10:00:00Z");
        var request = BuildFormRequest(ValidPaidFormBody());

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderRef.Should().Be(TestTransId);
        result.Value.State.Should().Be(PaymentState.Paid);
        result.Value.PaymentMethod.Should().Be("CARD_CZ");
    }

    [Fact]
    public async Task Body_status_PAID_but_reFetch_returns_PENDING_returns_reFetched_state_and_logs_Warning()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PENDING");
        var fields = ValidPaidFormBody();
        fields["status"] = "PAID"; // body says PAID, re-fetch says PENDING
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(PaymentState.Pending,
            "the authoritative re-fetched state must override the body");
        _loggerProvider.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("diverges"));
    }

    [Theory]
    [InlineData("PAID", PaymentState.Paid)]
    [InlineData("CANCELLED", PaymentState.Cancelled)]
    [InlineData("AUTHORIZED", PaymentState.Authorized)]
    [InlineData("PENDING", PaymentState.Pending)]
    public async Task State_mapping_matches_VerifyPayment_mapping(string comgateStatus, PaymentState expected)
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&status={comgateStatus}");
        var fields = ValidPaidFormBody();
        fields["status"] = comgateStatus;
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(expected);
    }

    // ---- Test-mode mismatch ----

    [Fact]
    public async Task Body_test_true_but_config_TestMode_false_logs_Critical_and_processes()
    {
        // SUT is configured TestMode=false (default); body says test=true.
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK, "code=0&status=PAID");
        var fields = ValidPaidFormBody();
        fields["test"] = "true";
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _loggerProvider.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical && e.Message.Contains("test-mode mismatch"));
    }

    [Fact]
    public async Task Body_test_false_but_config_TestMode_true_logs_Critical_and_processes()
    {
        BuildSut(testMode: true); // override default
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK, "code=0&status=PAID");
        var fields = ValidPaidFormBody();
        fields["test"] = "false";
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _loggerProvider.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical && e.Message.Contains("test-mode mismatch"));
    }

    // ---- Malformed-body branches ----

    [Fact]
    public async Task Missing_transId_returns_PaymentWebhookMalformed()
    {
        var fields = ValidPaidFormBody();
        fields.Remove("transId");
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentWebhookMalformed);
        result.Error.Type.Should().Be(ErrorType.Validation);
        // Adapter must not have called VerifyPayment.
        _handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Missing_refId_returns_PaymentWebhookMalformed()
    {
        var fields = ValidPaidFormBody();
        fields.Remove("refId");
        var request = BuildFormRequest(fields);

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentWebhookMalformed);
    }

    [Fact]
    public async Task Non_form_content_type_returns_PaymentWebhookMalformed()
    {
        var request = BuildJsonRequest("{ \"transId\": \"abc\" }");

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentWebhookMalformed);
        _handler.CallCount.Should().Be(0);
    }

    // ---- VerifyPayment failure propagation ----

    [Fact]
    public async Task VerifyPayment_returns_Transient_propagated_verbatim_not_remapped()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var request = BuildFormRequest(ValidPaidFormBody());

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task VerifyPayment_returns_Configuration_propagated_verbatim()
    {
        // Comgate code=1100 (bad merchant) is Configuration.
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1100&message=Bad+merchant");
        var request = BuildFormRequest(ValidPaidFormBody());

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderMisconfigured);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    // ---- T-0067 — PaidAt plumbing through WebhookPayload ----

    [Fact]
    public async Task ParseAndVerifyWebhookAsync_propagates_PaidAt_from_VerifyPaymentAsync()
    {
        // Comgate's status response carries paymentTime when status=PAID.
        // The adapter's VerifyPaymentAsync parses it; T-0067 extends
        // ParseAndVerifyWebhookAsync so the WebhookPayload now exposes it.
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PAID&method=CARD_CZ&paymentTime=2026-06-06T09:59:30Z");
        var request = BuildFormRequest(ValidPaidFormBody());

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidAt.Should().Be(
            new DateTimeOffset(2026, 6, 6, 9, 59, 30, TimeSpan.Zero),
            "T-0067 — the WebhookPayload now exposes Comgate's authoritative paymentTime");
    }

    [Fact]
    public async Task ParseAndVerifyWebhookAsync_with_null_PaidAt_from_VerifyPayment_returns_null_in_WebhookPayload()
    {
        // Provider returns PAID without a paymentTime field — should
        // surface as null PaidAt on the WebhookPayload, NOT crash and
        // NOT fall back to anything else (the consumer/handler is
        // responsible for the clock.UtcNow fallback).
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PAID&method=CARD_CZ");
        var request = BuildFormRequest(ValidPaidFormBody());

        var result = await _sut.ParseAndVerifyWebhookAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaidAt.Should().BeNull(
            "no paymentTime in the provider response = null on the payload");
    }

    // ---- Stub HTTP handler + in-memory logger (copied from
    //      ComgatePaymentProviderTests so the two suites stay independent.) ----

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Response);
        }
    }

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();
        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_entries);
        public void Dispose() { }

        private sealed class InMemoryLogger(ConcurrentQueue<LogEntry> sink) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
                NoopDisposable.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                sink.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new();
                public void Dispose() { }
            }
        }
    }

    public sealed record LogEntry(LogLevel Level, string Message);
}
