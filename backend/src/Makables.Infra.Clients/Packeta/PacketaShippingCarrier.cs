using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Makables.Infra.Clients.Packeta;

/// <summary>
/// Packeta carrier adapter per ADR 0017 + T-0070. Implements
/// <see cref="IShippingCarrier"/> over the Packeta REST API (v6).
///
/// <list type="bullet">
///   <item><description><see cref="CreateShipmentAsync"/> — POST
///     <c>{BaseUrl}/v6/createPacket</c>; returns the carrier id +
///     pre-computed tracking URL.</description></item>
///   <item><description><see cref="GetStatusAsync"/> — POST
///     <c>{BaseUrl}/v6/packetStatus</c>; maps Packeta state strings into
///     <see cref="ShipmentState"/>.</description></item>
///   <item><description><see cref="GetLabelPdfAsync"/> — POST
///     <c>{BaseUrl}/v6/packetLabelPdf</c>; returns the PDF Stream.
///     Caller disposes.</description></item>
///   <item><description><see cref="WidgetConfig"/> — pure data lookup,
///     no I/O.</description></item>
/// </list>
///
/// <para>
/// <b>Secret discipline.</b> The Packeta <c>apiPassword</c> is the auth
/// credential; sent in the request body only, never headers, never URL,
/// never logs. Tests pin this.
/// </para>
///
/// <para>
/// <b>Resilience.</b> Calls flow through the shared
/// <c>ResiliencePipelineRegistry&lt;string&gt;</c> keyed by
/// <see cref="HttpClientName"/>. Retries 3× with exponential jittered
/// backoff (200ms base) on transient HTTP failures. After exhaustion the
/// caller sees <see cref="BusinessErrorMessage.ShippingCarrierUnavailable"/>.
/// </para>
///
/// <para>
/// <b>Error classification</b> (ADR 0016 §A.14 / T-0070 §B):
/// 5xx + timeout → Transient; 4xx with body keyword <c>address</c> →
/// Permanent(<c>ShippingCarrierAddressIdNotFound</c>); 4xx with body
/// keyword <c>weight</c> → Permanent(<c>ShippingCarrierInvalidWeight</c>);
/// 401/403 → Configuration(<c>ShippingCarrierConfigurationError</c>);
/// anything else 4xx → Permanent(<c>ShippingCarrierAddressIdNotFound</c>)
/// as the conservative default.
/// </para>
/// </summary>
public sealed class PacketaShippingCarrier(
    IHttpClientFactory httpClientFactory,
    IOptions<PacketaOptions> options,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    ILogger<PacketaShippingCarrier> logger) : IShippingCarrier
{
    /// <summary>Named HttpClient registered by <c>AddMakablesClients</c>.</summary>
    public const string HttpClientName = "packeta";

    /// <summary>Carrier code; matches <c>CountryConfiguration.DefaultShippingCarrier</c>.</summary>
    public const string CarrierCode = "packeta";

    public string Code => CarrierCode;

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    public PickupPointWidgetConfig WidgetConfig(string locale, string countryCode)
    {
        var opts = options.Value;
        var widgetOptions = new Dictionary<string, string>
        {
            ["country"] = countryCode,
            ["language"] = locale,
        };
        return new PickupPointWidgetConfig(
            ScriptUrl: opts.WidgetScriptUrl,
            PublicKey: opts.PublicWidgetKey,
            Options: widgetOptions);
    }

    public async Task<BusinessResult<Shipment>> CreateShipmentAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var opts = options.Value;
        var (firstName, lastName) = SplitContactName(order.ContactName);

        // Packeta API v6 uses XML by default. Build a packetAttributes XML
        // doc per the public spec. Sensitive fields (apiPassword) live in
        // the body only.
        var packetAttributes = new XElement("packetAttributes",
            new XElement("number", order.OrderNumber),
            new XElement("name", firstName),
            new XElement("surname", lastName),
            new XElement("email", order.ContactEmail),
            new XElement("phone", order.ContactPhone),
            new XElement("addressId", order.ZasilkovnaPickupPointId ?? string.Empty),
            new XElement("cod", "0"),
            new XElement("value", (order.TotalAmountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture)),
            // Tracked: Product.Weight is not on the entity at MVP per
            // ADR 0017 risk register (§"Weight/value sourcing"). Weight is
            // platform-default 1.0 kg until a future ticket adds the field
            // + validation. See docs/adr/0017-shipping-packeta.md.
            new XElement("weight", "1.0"),
            new XElement("currency", order.Currency),
            new XElement("eshop", opts.SenderLabel));
        var requestRoot = new XElement("createPacket",
            new XElement("apiPassword", opts.ApiKey),
            packetAttributes);

        var responseText = await PostXmlAsync(
            $"{opts.BaseUrl.TrimEnd('/')}/v6/createPacket",
            requestRoot.ToString(),
            operationLabel: "createPacket",
            cancellationToken);
        if (!responseText.IsSuccess)
            return BusinessResult.Failure<Shipment>(responseText.Error!);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(responseText.Value!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Packeta.createPacket: XML parse failed.");
            return BusinessResult.Failure<Shipment>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var status = doc.Root?.Element("status")?.Value;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ClassifyPacketaFault<Shipment>(doc, "createPacket");
        }

        var idElement = doc.Root?.Element("result")?.Element("id")
                        ?? doc.Root?.Element("id");
        var carrierRef = idElement?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(carrierRef))
        {
            logger.LogError("Packeta.createPacket: status=ok but result.id missing.");
            return BusinessResult.Failure<Shipment>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var trackingUrl = $"https://tracking.packeta.com/Z{carrierRef}";
        logger.LogInformation(
            "Packeta.createPacket: shipment created for order {OrderId} (carrierRef={CarrierRef}).",
            order.Id, carrierRef);
        return BusinessResult.Success(new Shipment(carrierRef, trackingUrl));
    }

    public async Task<BusinessResult<Shipment>> CreateReturnShipmentAsync(
        Order order,
        ReturnRecipient recipient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(recipient);

        var opts = options.Value;
        var (firstName, lastName) = SplitContactName(recipient.Name);

        // Reverse leg (T-0146): same v6 createPacket surface as the
        // forward path, but the RECIPIENT is now the maker's registered
        // (or pickup) address — a door-delivery address rather than a
        // pickup-point id, since makers aren't Zásilkovna box holders.
        // The customer (who physically drops the parcel off) is the
        // conceptual "sender"; Packeta's packetAttributes payload only
        // ever names the final recipient, so no separate sender block
        // is sent — mirrors the forward call's shape.
        var packetAttributes = new XElement("packetAttributes",
            new XElement("number", $"{order.OrderNumber}-RET"),
            new XElement("name", firstName),
            new XElement("surname", lastName),
            new XElement("email", recipient.Email),
            new XElement("phone", recipient.Phone),
            new XElement("street", recipient.Street),
            new XElement("houseNumber", recipient.HouseNumber),
            new XElement("city", recipient.City),
            new XElement("zip", recipient.Zip),
            new XElement("country", recipient.CountryCodeIso),
            new XElement("cod", "0"),
            // No itemized reverse-leg price from Packeta at MVP — the
            // cost basis for the maker payout-batch deduction is computed
            // by the caller from CountryConfiguration.DefaultShippingPriceMinor
            // (T-0146 Technical notes); this call only creates the shipment.
            new XElement("value", "0.00"),
            new XElement("weight", "1.0"),
            new XElement("currency", order.Currency),
            new XElement("eshop", opts.SenderLabel));
        var requestRoot = new XElement("createPacket",
            new XElement("apiPassword", opts.ApiKey),
            packetAttributes);

        var responseText = await PostXmlAsync(
            $"{opts.BaseUrl.TrimEnd('/')}/v6/createPacket",
            requestRoot.ToString(),
            operationLabel: "createReturnPacket",
            cancellationToken);
        if (!responseText.IsSuccess)
            return BusinessResult.Failure<Shipment>(responseText.Error!);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(responseText.Value!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Packeta.createReturnPacket: XML parse failed.");
            return BusinessResult.Failure<Shipment>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var status = doc.Root?.Element("status")?.Value;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ClassifyPacketaFault<Shipment>(doc, "createReturnPacket");
        }

        var idElement = doc.Root?.Element("result")?.Element("id")
                        ?? doc.Root?.Element("id");
        var carrierRef = idElement?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(carrierRef))
        {
            logger.LogError("Packeta.createReturnPacket: status=ok but result.id missing.");
            return BusinessResult.Failure<Shipment>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var trackingUrl = $"https://tracking.packeta.com/Z{carrierRef}";
        logger.LogInformation(
            "Packeta.createReturnPacket: reverse shipment created for order {OrderId} (carrierRef={CarrierRef}).",
            order.Id, carrierRef);
        return BusinessResult.Success(new Shipment(carrierRef, trackingUrl));
    }

    public async Task<BusinessResult<ShipmentStatus>> GetStatusAsync(
        string carrierRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(carrierRef))
            throw new ArgumentException("CarrierRef is required.", nameof(carrierRef));

        var opts = options.Value;
        var requestRoot = new XElement("packetStatus",
            new XElement("apiPassword", opts.ApiKey),
            new XElement("packetId", carrierRef.Trim()));

        var responseText = await PostXmlAsync(
            $"{opts.BaseUrl.TrimEnd('/')}/v6/packetStatus",
            requestRoot.ToString(),
            operationLabel: "packetStatus",
            cancellationToken);
        if (!responseText.IsSuccess)
            return BusinessResult.Failure<ShipmentStatus>(responseText.Error!);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(responseText.Value!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Packeta.packetStatus: XML parse failed.");
            return BusinessResult.Failure<ShipmentStatus>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var status = doc.Root?.Element("status")?.Value;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            return ClassifyPacketaFault<ShipmentStatus>(doc, "packetStatus");

        var statusName = doc.Root?.Element("result")?.Element("statusName")?.Value
                         ?? doc.Root?.Element("result")?.Element("codeText")?.Value
                         ?? string.Empty;
        var state = MapPacketaStatus(statusName);
        DateTimeOffset? deliveredAt = null;
        if (state == ShipmentState.Delivered)
        {
            var dateText = doc.Root?.Element("result")?.Element("dateTime")?.Value;
            if (!string.IsNullOrWhiteSpace(dateText)
                && DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                deliveredAt = parsed;
            }
        }
        return BusinessResult.Success(new ShipmentStatus(state, deliveredAt));
    }

    public async Task<BusinessResult<Stream>> GetLabelPdfAsync(
        string carrierRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(carrierRef))
            throw new ArgumentException("CarrierRef is required.", nameof(carrierRef));

        var opts = options.Value;
        var requestRoot = new XElement("packetLabelPdf",
            new XElement("apiPassword", opts.ApiKey),
            new XElement("packetId", carrierRef.Trim()),
            new XElement("format", "A6 on A4"),
            new XElement("offset", "0"));

        var http = httpClientFactory.CreateClient(HttpClientName);
        var requestBody = requestRoot.ToString();

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    var content = new StringContent(requestBody, Encoding.UTF8, "text/xml");
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        $"{opts.BaseUrl.TrimEnd('/')}/v6/packetLabelPdf")
                    { Content = content };
                    return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Packeta.packetLabelPdf: HTTP error after retries.");
            return BusinessResult.Failure<Stream>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Packeta.packetLabelPdf: timeout after retries.");
            return BusinessResult.Failure<Stream>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
        }

        if (!response.IsSuccessStatusCode)
        {
            var faultResult = await ClassifyHttpFailureAsync<Stream>(response, "packetLabelPdf", cancellationToken);
            response.Dispose();
            return faultResult;
        }

        // Success: stream the PDF bytes back to the caller. The Stream owns
        // the response disposal — when the caller disposes the returned
        // Stream the underlying response (and socket) close.
        var pdfStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return BusinessResult.Success<Stream>(new OwningStream(pdfStream, response));
    }

    // ---- helpers ----

    private async Task<BusinessResult<string>> PostXmlAsync(
        string url, string requestXml, string operationLabel, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogError("Packeta options are not configured (ApiKey missing).");
            return BusinessResult.Failure<string>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    var content = new StringContent(requestXml, Encoding.UTF8, "text/xml");
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                    return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Packeta.{Operation}: HTTP error after retries.", operationLabel);
            return BusinessResult.Failure<string>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Packeta.{Operation}: timeout after retries.", operationLabel);
            return BusinessResult.Failure<string>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ClassifyHttpFailureAsync<string>(response, operationLabel, cancellationToken);
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return BusinessResult.Success(body);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Packeta.{Operation}: failed to read response body.", operationLabel);
                return BusinessResult.Failure<string>(
                    Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
            }
        }
    }

    private async Task<BusinessResult<T>> ClassifyHttpFailureAsync<T>(
        HttpResponseMessage response, string operationLabel, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            body = string.Empty;
        }

        logger.LogWarning(
            "Packeta.{Operation}: HTTP {Status}.",
            operationLabel, status);

        if (status is 401 or 403)
        {
            logger.LogCritical(
                "Packeta.{Operation}: misconfigured credentials (HTTP {Status}).",
                operationLabel, status);
            return BusinessResult.Failure<T>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }

        if (status is >= 500 and <= 599 or 408 or 429)
        {
            return BusinessResult.Failure<T>(
                Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable));
        }

        // 4xx — classify by body keyword.
        var lowerBody = body.ToLowerInvariant();
        if (lowerBody.Contains("address") || lowerBody.Contains("pickup"))
        {
            return BusinessResult.Failure<T>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierAddressIdNotFound));
        }
        if (lowerBody.Contains("weight"))
        {
            return BusinessResult.Failure<T>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierInvalidWeight));
        }
        // Conservative default for unknown 4xx.
        return BusinessResult.Failure<T>(
            Error.Permanent(BusinessErrorMessage.ShippingCarrierAddressIdNotFound));
    }

    private BusinessResult<T> ClassifyPacketaFault<T>(XDocument doc, string operationLabel)
    {
        var fault = doc.Root?.Element("result")?.Element("fault")?.Value
                    ?? doc.Root?.Element("string")?.Value
                    ?? string.Empty;
        logger.LogWarning(
            "Packeta.{Operation}: status=fault fault={Fault}.",
            operationLabel, fault);

        var lower = fault.ToLowerInvariant();
        if (lower.Contains("auth") || lower.Contains("password") || lower.Contains("unauthor"))
        {
            return BusinessResult.Failure<T>(
                Error.Configuration(BusinessErrorMessage.ShippingCarrierConfigurationError));
        }
        if (lower.Contains("weight"))
        {
            return BusinessResult.Failure<T>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierInvalidWeight));
        }
        if (lower.Contains("address") || lower.Contains("pickup") || lower.Contains("branch"))
        {
            return BusinessResult.Failure<T>(
                Error.Permanent(BusinessErrorMessage.ShippingCarrierAddressIdNotFound));
        }
        return BusinessResult.Failure<T>(
            Error.Permanent(BusinessErrorMessage.ShippingCarrierAddressIdNotFound));
    }

    private static (string FirstName, string LastName) SplitContactName(string fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace <= 0) return (trimmed, "-");
        var first = trimmed[..lastSpace].Trim();
        var last = trimmed[(lastSpace + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(last)) last = "-";
        return (first, last);
    }

    private static ShipmentState MapPacketaStatus(string statusName)
    {
        var lower = (statusName ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("doruč") || lower.Contains("deliver")) return ShipmentState.Delivered;
        if (lower.Contains("vrat") || lower.Contains("return")) return ShipmentState.Returned;
        if (lower.Contains("fail") || lower.Contains("ztrac") || lower.Contains("storn"))
            return ShipmentState.Failed;
        if (lower.Contains("přepravě") || lower.Contains("transit") || lower.Contains("branch")
            || lower.Contains("výdej") || lower.Contains("received"))
            return ShipmentState.InTransit;
        return ShipmentState.Created;
    }

    /// <summary>
    /// Stream wrapper that disposes the parent <see cref="HttpResponseMessage"/>
    /// when the stream itself is disposed. Lets the caller treat the returned
    /// <see cref="Stream"/> as a normal resource without leaking the underlying
    /// socket.
    /// </summary>
    private sealed class OwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public OwningStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            await base.DisposeAsync();
        }
    }
}
