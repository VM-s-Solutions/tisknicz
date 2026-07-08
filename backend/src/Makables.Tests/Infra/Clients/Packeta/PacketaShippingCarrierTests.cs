using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Infra.Clients.Packeta;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace Makables.Tests.Infra.Clients.Packeta;

/// <summary>
/// Pins the T-0070 Packeta carrier adapter contract. Covers:
/// widget-config pure lookup, CreateShipmentAsync XML success, error
/// classification (5xx → Transient, 401/403 → Configuration,
/// 4xx-with-address → Permanent.AddressIdNotFound, 4xx-with-weight →
/// Permanent.InvalidWeight, timeout → Transient), GetStatusAsync
/// state mapping for the documented Packeta state labels, and
/// GetLabelPdfAsync stream success.
///
/// <para>
/// The adapter uses Packeta's REST v6 surface which is XML over POST —
/// the request/response bodies are XML, not JSON, so the test fixtures
/// build XML doc fragments that mirror the production wire format
/// (<c>&lt;response&gt;&lt;status&gt;ok&lt;/status&gt;&lt;result&gt;&lt;id&gt;...&lt;/id&gt;&lt;/result&gt;&lt;/response&gt;</c>).
/// </para>
///
/// <para>
/// HTTP mocking pattern mirrors <see cref="Comgate.ComgatePaymentProviderTests"/>
/// — a <see cref="StubHttpMessageHandler"/> via NSubstitute-style scripted
/// responses + a zero-retry Polly pipeline so each test exercises a single
/// HTTP call unless explicitly testing retries.
/// </para>
/// </summary>
public class PacketaShippingCarrierTests
{
    private const string TestApiKey = "test-api-password";
    private const string TestPublicWidgetKey = "public-widget-key-123";
    private const string TestBaseUrl = "https://api.packeta.test";
    private const string TestWidgetScriptUrl = "https://widget.packeta.test/v6/library.js";
    private const string TestSenderLabel = "makables-test";
    private const string TestCarrierRef = "9876543210";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly PacketaShippingCarrier _sut;

    public PacketaShippingCarrierTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PacketaShippingCarrier.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new PacketaOptions
        {
            ApiKey = TestApiKey,
            PublicWidgetKey = TestPublicWidgetKey,
            BaseUrl = TestBaseUrl,
            WidgetScriptUrl = TestWidgetScriptUrl,
            SenderLabel = TestSenderLabel,
        });

        // Zero-retry registry — each test exercises a single HTTP call.
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            PacketaShippingCarrier.HttpClientName,
            (builder, _) => { /* no-op: no retry by default */ });

        _sut = new PacketaShippingCarrier(
            factory,
            opts,
            registry,
            NullLogger<PacketaShippingCarrier>.Instance);
    }

    private static Order BuildOrder() => Order.Create(
        id: "ord-1",
        orderNumber: "M-CZ-20260042",
        customerUserId: "user-1",
        makerId: "maker-1",
        productId: "prod-1",
        contactName: "Anna Nováková",
        contactEmail: "anna@example.cz",
        contactPhone: "+420 777 123 456",
        productPriceAmountMinor: 50000,
        shippingPriceAmountMinor: 7900,
        platformFeeAmountMinor: 7500,
        makerPayoutAmountMinor: 50400,
        totalAmountMinor: 57900,
        currency: "CZK",
        vatRateBp: 2100,
        shippingMethod: ShippingMethod.ZasilkovnaPickupPoint,
        zasilkovnaPickupPointId: "pp-42",
        countryCode: "CZ");

    private static HttpResponseMessage Xml(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml"),
        };

    private static string PacketaOkResponse(string id) =>
        "<response>" +
        "<status>ok</status>" +
        $"<result><id>{id}</id></result>" +
        "</response>";

    private static string PacketaStatusResponse(string statusName) =>
        "<response>" +
        "<status>ok</status>" +
        $"<result><statusName>{statusName}</statusName></result>" +
        "</response>";

    // ---- WidgetConfig ----

    [Fact]
    public void WidgetConfig_returns_config_with_PublicKey_ScriptUrl_and_Options()
    {
        var config = _sut.WidgetConfig("cs-CZ", "CZ");

        config.PublicKey.Should().Be(TestPublicWidgetKey);
        config.ScriptUrl.Should().Be(TestWidgetScriptUrl);
        config.Options.Should().ContainKey("country").WhoseValue.Should().Be("CZ");
        config.Options.Should().ContainKey("language").WhoseValue.Should().Be("cs-CZ");
    }

    // ---- CreateShipmentAsync ----

    [Fact]
    public async Task CreateShipmentAsync_success_returns_Shipment_with_correct_TrackingUrl()
    {
        _handler.Response = Xml(HttpStatusCode.OK, PacketaOkResponse(TestCarrierRef));

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CarrierRef.Should().Be(TestCarrierRef);
        result.Value.TrackingUrl.Should().Be($"https://tracking.packeta.com/Z{TestCarrierRef}");
    }

    [Fact]
    public async Task CreateShipmentAsync_503_returns_Transient_ShippingCarrierUnavailable()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task CreateShipmentAsync_4xx_with_address_id_not_found_body_returns_Permanent_ShippingCarrierAddressIdNotFound()
    {
        _handler.Response = Xml(HttpStatusCode.BadRequest, "address id not found");

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierAddressIdNotFound);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task CreateShipmentAsync_4xx_with_weight_body_returns_Permanent_ShippingCarrierInvalidWeight()
    {
        _handler.Response = Xml(HttpStatusCode.BadRequest, "weight exceeds maximum");

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierInvalidWeight);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task CreateShipmentAsync_401_returns_Configuration_ShippingCarrierConfigurationError()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task CreateShipmentAsync_403_returns_Configuration_ShippingCarrierConfigurationError()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.Forbidden);

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task CreateShipmentAsync_timeout_returns_Transient_ShippingCarrierUnavailable()
    {
        // Production code catches TaskCanceledException when the caller's
        // CancellationToken is NOT the cancelled one (i.e. timeout, not
        // user-initiated cancellation). We pass CancellationToken.None
        // and throw a TaskCanceledException from the handler — that
        // mirrors the HttpClient.Timeout behaviour.
        _handler.OnSend = (_, _) => throw new TaskCanceledException("simulated timeout");

        var result = await _sut.CreateShipmentAsync(BuildOrder(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    // ---- CreateReturnShipmentAsync (T-0146) ----

    private static ReturnRecipient BuildRecipient() => new(
        Name: "Studio Keramika s.r.o.",
        Email: "maker@example.cz",
        Phone: "+420 606 111 222",
        Street: "Dílenská",
        HouseNumber: "12",
        City: "Brno",
        Zip: "60200",
        CountryCodeIso: "CZ");

    [Fact]
    public async Task CreateReturnShipmentAsync_success_returns_Shipment_with_correct_TrackingUrl()
    {
        _handler.Response = Xml(HttpStatusCode.OK, PacketaOkResponse(TestCarrierRef));

        var result = await _sut.CreateReturnShipmentAsync(BuildOrder(), BuildRecipient(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CarrierRef.Should().Be(TestCarrierRef);
        result.Value.TrackingUrl.Should().Be($"https://tracking.packeta.com/Z{TestCarrierRef}");
    }

    [Fact]
    public async Task CreateReturnShipmentAsync_503_returns_Transient_ShippingCarrierUnavailable()
    {
        _handler.Response = Xml(HttpStatusCode.ServiceUnavailable, string.Empty);

        var result = await _sut.CreateReturnShipmentAsync(BuildOrder(), BuildRecipient(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierUnavailable);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Fact]
    public async Task CreateReturnShipmentAsync_401_returns_Configuration_ShippingCarrierConfigurationError()
    {
        _handler.Response = Xml(HttpStatusCode.Unauthorized, string.Empty);

        var result = await _sut.CreateReturnShipmentAsync(BuildOrder(), BuildRecipient(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task CreateReturnShipmentAsync_sends_recipient_as_the_packet_destination()
    {
        string? capturedBody = null;
        _handler.OnSend = (request, _) =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Xml(HttpStatusCode.OK, PacketaOkResponse(TestCarrierRef));
        };

        await _sut.CreateReturnShipmentAsync(BuildOrder(), BuildRecipient(), CancellationToken.None);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("Dílenská");
        capturedBody.Should().Contain("Brno");
        capturedBody.Should().Contain("60200");
        capturedBody.Should().Contain("maker@example.cz");
    }

    // ---- GetStatusAsync ----

    [Theory]
    // The production MapPacketaStatus checks for substrings in priority
    // order: deliver/doruč → Delivered, vrat/return → Returned,
    // fail/ztrac/storn → Failed, transit/branch/received/výdej/přepravě
    // → InTransit, else → Created. The labels below pick disjoint
    // substrings so each maps to exactly one enum value.
    [InlineData("created", ShipmentState.Created)]                  // no keyword → fall through to Created
    [InlineData("in transit", ShipmentState.InTransit)]             // matches "transit"
    [InlineData("delivered", ShipmentState.Delivered)]              // matches "deliver"
    [InlineData("returned", ShipmentState.Returned)]                // matches "return"
    [InlineData("failed", ShipmentState.Failed)]                    // matches "fail"
    public async Task GetStatusAsync_maps_Packeta_state_strings_to_ShipmentState_enum(
        string packetaLabel, ShipmentState expected)
    {
        _handler.Response = Xml(HttpStatusCode.OK, PacketaStatusResponse(packetaLabel));

        var result = await _sut.GetStatusAsync(TestCarrierRef, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(expected);
    }

    // ---- GetLabelPdfAsync ----

    [Fact]
    public async Task GetLabelPdfAsync_returns_Stream_on_success()
    {
        // Minimal PDF body. The adapter wraps the response stream in an
        // OwningStream that disposes the HttpResponseMessage on dispose;
        // the caller only sees the wrapped Stream.
        var pdfBytes = "%PDF-1.7\nfake-content"u8.ToArray();
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfBytes),
        };

        var result = await _sut.GetLabelPdfAsync(TestCarrierRef, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await using var stream = result.Value!;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.ToArray().Should().Equal(pdfBytes);
    }

    // ---- Stub HTTP handler ----

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? OnSend { get; set; }
        public ConcurrentBag<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (OnSend is not null) return Task.FromResult(OnSend(request, cancellationToken));
            return Task.FromResult(Response);
        }
    }
}
