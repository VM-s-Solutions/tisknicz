using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Comgate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;

namespace Makables.Tests.Infra.Clients.Comgate;

/// <summary>
/// Pins the Comgate adapter contract per ADR 0016 / T-0065. Covers:
/// happy paths on create + verify + refund (T-0105), secret-discipline
/// (never in URL, never in log scope), error mapping (5xx → Transient,
/// code=1100/1102 → Configuration + Critical, code != 0 known business
/// error → Permanent), and Polly retry behaviour.
/// Webhook parsing coverage lives in
/// <see cref="ComgatePaymentProviderWebhookTests"/> (T-0066).
/// </summary>
public class ComgatePaymentProviderTests
{
    private const string TestMerchant = "12345";
    private const string TestSecret = "super-secret-do-not-log";
    private const string TestBaseUrl = "https://payments.comgate.test";
    private const string TestTransId = "AB1C-D34E";
    private const string TestRedirect = "https://payments.comgate.test/pay/AB1C-D34E";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");

    private readonly StubHttpMessageHandler _handler = new();
    private readonly InMemoryLoggerProvider _loggerProvider = new();
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly ComgatePaymentProvider _sut;

    public ComgatePaymentProviderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ComgatePaymentProvider.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new ComgateOptions
        {
            MerchantId = TestMerchant,
            Secret = TestSecret,
            BaseUrl = TestBaseUrl,
        });

        // Zero-retry registry — each test exercises a single HTTP call
        // unless explicitly testing retries.
        var registry = new Polly.Registry.ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            ComgatePaymentProvider.HttpClientName,
            (builder, _) => { /* no-op: no retry by default */ });

        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildCzConfig());

        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(_loggerProvider));
        var logger = loggerFactory.CreateLogger<ComgatePaymentProvider>();

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        _sut = new ComgatePaymentProvider(factory, opts, _configs, registry, clock, logger);
    }

    private static CountryConfiguration BuildCzConfig() => CountryConfiguration.Create(
        countryId: "CZ",
        defaultCurrencyCode: "CZK",
        defaultLanguageCode: "cs-CZ",
        timeZoneId: "Europe/Prague",
        phonePrefix: "+420",
        dateFormat: "d. M. yyyy",
        standardVatRateBp: 2100,
        taxIdLabel: "DIČ",
        vatIdLabel: "DIČ DPH",
        registrationNumberLabel: "IČO",
        defaultPaymentProvider: "comgate",
        defaultShippingCarrier: "packeta",
        defaultRegistry: "ares",
        defaultEmailProvider: "sendgrid",    issuerName: "JVM YORE s.r.o.",    issuerIco: "00000000");

    private static Order BuildOrder(
        long totalMinor = 57900,
        string currency = "CZK",
        string country = "CZ") => Order.Create(
        id: "ord-1",
        orderNumber: "M-CZ-20260042",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Anna",
        contactEmail: "anna@example.cz",
        contactPhone: "+420 777 123 456",
        productPriceAmountMinor: 50000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500,
        makerPayoutAmountMinor: 50400,
        totalAmountMinor: totalMinor,
        currency: currency,
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42",
        countryCode: country);

    private static HttpResponseMessage FormUrlEncoded(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

    // ---- CreatePaymentAsync ----

    [Fact]
    public async Task CreatePayment_happy_path_extracts_transId_and_redirect()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&message=OK&transId={TestTransId}&redirect={Uri.EscapeDataString(TestRedirect)}");

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderRef.Should().Be(TestTransId);
        result.Value.RedirectUrl.Should().Be(TestRedirect);
    }

    [Fact]
    public async Task CreatePayment_posts_required_body_fields()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&transId={TestTransId}&redirect=https://x");

        await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        var body = _handler.LastRequestBody;
        body.Should().Contain("merchant=12345");
        body.Should().Contain("price=57900");
        body.Should().Contain("curr=CZK");
        body.Should().Contain($"refId=ord-1");
        body.Should().Contain("prepareOnly=true");
        body.Should().Contain("country=CZ");
        // Comgate wants 2-letter language code, derived from cs-CZ.
        body.Should().Contain("lang=cs");
        body.Should().Contain("method=ALL");
        body.Should().Contain($"secret={TestSecret}");
        // label = "Objednávka <number>" — URL-encoded space + diacritic.
        body.Should().Contain("Objedn");
        body.Should().Contain("M-CZ-20260042");
    }

    [Fact]
    public async Task CreatePayment_secret_never_appears_in_URL()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&transId={TestTransId}&redirect=https://x");

        await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        var url = _handler.LastRequestUri!.ToString();
        url.Should().NotContain("secret", "the secret MUST NOT appear in the URL");
        url.Should().NotContain(TestSecret, "the secret value MUST NOT appear in the URL");
    }

    [Fact]
    public async Task CreatePayment_secret_never_appears_in_any_log_scope()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&transId={TestTransId}&redirect=https://x");

        await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        _loggerProvider.AllLogText.Should().NotContain(TestSecret,
            "the secret must never appear in any log message or scope");
    }

    [Fact]
    public async Task CreatePayment_code_1100_bad_merchant_is_Configuration_with_Critical_log()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1100&message=Bad+merchant");

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderMisconfigured);
        result.Error.Type.Should().Be(ErrorType.Configuration);
        _loggerProvider.Entries.Should().Contain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task CreatePayment_code_1102_bad_secret_is_Configuration_with_Critical_log()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1102&message=Invalid+secret");

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderMisconfigured);
        result.Error.Type.Should().Be(ErrorType.Configuration);
        _loggerProvider.Entries.Should().Contain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task CreatePayment_business_error_code_is_Permanent_PaymentProviderRejected()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1300&message=Insufficient+merchant+balance");

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderRejected);
        result.Error.Type.Should().Be(ErrorType.Permanent);
        _loggerProvider.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(408)]
    [InlineData(429)]
    public async Task CreatePayment_HTTP_transient_status_is_PaymentProviderUnavailable_Transient(int status)
    {
        _handler.Response = new HttpResponseMessage((HttpStatusCode)status);

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task CreatePayment_HttpRequestException_is_PaymentProviderUnavailable_Transient()
    {
        _handler.OnSend = (_, _) => throw new HttpRequestException("connection reset");

        var result = await _sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task CreatePayment_with_retrying_pipeline_recovers_after_503()
    {
        // Build a 3-attempt retry pipeline matching the production wiring.
        var registry = new Polly.Registry.ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            ComgatePaymentProvider.HttpClientName,
            (builder, _) => builder.AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(1),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode is 408 or 429 or >= 500 and <= 599),
            }));

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ComgatePaymentProvider.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new ComgateOptions
        {
            MerchantId = TestMerchant, Secret = TestSecret, BaseUrl = TestBaseUrl,
        });

        var configs = Substitute.For<ICountryConfigurationRepository>();
        configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(BuildCzConfig());

        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(_loggerProvider));
        var logger = loggerFactory.CreateLogger<ComgatePaymentProvider>();
        var retryClock = Substitute.For<IClock>();
        retryClock.UtcNow.Returns(Now);
        var sut = new ComgatePaymentProvider(factory, opts, configs, registry, retryClock, logger);

        // First call fails 503, second succeeds.
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            FormUrlEncoded(HttpStatusCode.OK,
                $"code=0&transId={TestTransId}&redirect={Uri.EscapeDataString(TestRedirect)}"),
        });
        _handler.OnSend = (_, _) => responses.Dequeue();

        var result = await sut.CreatePaymentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _handler.CallCount.Should().Be(2);
    }

    // ---- VerifyPaymentAsync ----

    [Fact]
    public async Task VerifyPayment_happy_path_PAID_with_payment_time_is_mapped()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PAID&method=CARD_CZ_CSOB&paymentTime=2026-06-05T10:00:00Z");

        var result = await _sut.VerifyPaymentAsync(TestTransId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(PaymentState.Paid);
        result.Value.PaymentMethod.Should().Be("CARD_CZ_CSOB");
        result.Value.PaidAt.Should().Be(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task VerifyPayment_PENDING_returns_Pending_state_with_null_PaidAt()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=0&status=PENDING&method=CARD_CZ_CSOB");

        var result = await _sut.VerifyPaymentAsync(TestTransId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(PaymentState.Pending);
        result.Value.PaidAt.Should().BeNull();
    }

    [Theory]
    [InlineData("AUTHORIZED", PaymentState.Authorized)]
    [InlineData("CANCELLED", PaymentState.Cancelled)]
    public async Task VerifyPayment_maps_other_known_statuses(string comgate, PaymentState expected)
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            $"code=0&status={comgate}");

        var result = await _sut.VerifyPaymentAsync(TestTransId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(expected);
    }

    // ---- RefundAsync (T-0105) ----

    [Fact]
    public async Task Refund_happy_path_returns_receipt_with_clock_timestamp_and_original_transId()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK, "code=0&message=OK");

        var result = await _sut.RefundAsync(TestTransId, 1000, "CZK", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Comgate's refund response has no distinct refund id — the
        // receipt echoes the original transId (T-0105 §C).
        result.Value!.RefundProviderRef.Should().Be(TestTransId);
        result.Value.AmountMinor.Should().Be(1000);
        result.Value.Currency.Should().Be("CZK");
        result.Value.RefundedAt.Should().Be(Now, "entity-grade time discipline via IClock");
    }

    [Fact]
    public async Task Refund_posts_required_body_fields_and_keeps_secret_out_of_URL()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK, "code=0&message=OK");

        await _sut.RefundAsync(TestTransId, 1000, "CZK", CancellationToken.None);

        var body = _handler.LastRequestBody;
        body.Should().Contain("merchant=12345");
        body.Should().Contain($"transId={TestTransId}");
        body.Should().Contain("amount=1000");
        body.Should().Contain("curr=CZK");
        body.Should().Contain($"secret={TestSecret}");

        var url = _handler.LastRequestUri!.ToString();
        url.Should().EndWith("/v1.0/refund");
        url.Should().NotContain("secret", "the secret MUST NOT appear in the URL");
        _loggerProvider.AllLogText.Should().NotContain(TestSecret,
            "the secret must never appear in any log message or scope");
    }

    [Fact]
    public async Task Refund_business_rejection_is_Permanent_PaymentProviderRejected()
    {
        // e.g. refund window expired / amount above capture.
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1400&message=Refund+rejected");

        var result = await _sut.RefundAsync(TestTransId, 1000, "CZK", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderRejected);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Refund_bad_credentials_is_Configuration_with_Critical_log()
    {
        _handler.Response = FormUrlEncoded(HttpStatusCode.OK,
            "code=1102&message=Invalid+secret");

        var result = await _sut.RefundAsync(TestTransId, 1000, "CZK", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderMisconfigured);
        result.Error.Type.Should().Be(ErrorType.Configuration);
        _loggerProvider.Entries.Should().Contain(e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task Refund_5xx_is_Transient_PaymentProviderUnavailable()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.RefundAsync(TestTransId, 1000, "CZK", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task Refund_throws_on_non_positive_amount(long amountMinor)
    {
        var act = () => _sut.RefundAsync(TestTransId, amountMinor, "CZK", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- Stub HTTP handler + in-memory logger ----

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public HttpRequestMessage? LastRequest { get; private set; }
        /// <summary>
        /// Captured body of the last request — read here so it survives the
        /// production code's `using` on HttpRequestMessage (which disposes
        /// the form content the moment SendAsync returns).
        /// </summary>
        public string LastRequestBody { get; private set; } = string.Empty;
        public Uri? LastRequestUri { get; private set; }
        public int CallCount { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? OnSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            if (OnSend is not null) return OnSend(request, cancellationToken);
            return Response;
        }
    }

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public string AllLogText => string.Join("\n", _entries.Select(e => e.Message));

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
