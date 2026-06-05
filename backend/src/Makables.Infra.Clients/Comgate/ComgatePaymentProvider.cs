using System.Globalization;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Polly;
using Polly.Registry;

namespace Makables.Infra.Clients.Comgate;

/// <summary>
/// Comgate payment gateway adapter per ADR 0016. Implements
/// <see cref="IPaymentProvider"/>:
///
/// <list type="bullet">
///   <item><description><see cref="CreatePaymentAsync"/> — form-urlencoded
///     POST to <c>{BaseUrl}/v1.0/create</c>; returns the redirect URL and
///     <c>transId</c>.</description></item>
///   <item><description><see cref="VerifyPaymentAsync"/> — GET
///     <c>{BaseUrl}/v1.0/status</c>; maps Comgate <c>status</c> into
///     <see cref="PaymentState"/>.</description></item>
///   <item><description><see cref="ParseAndVerifyWebhookAsync"/> +
///     <see cref="RefundAsync"/> — throw <see cref="NotSupportedException"/>
///     with the T-0066 / T-0105 ticket reference per user decision Q2.</description></item>
/// </list>
///
/// <para>
/// <b>Secret discipline.</b> The Comgate <c>secret</c> form field is the
/// auth credential. It MUST appear ONLY in the request body — never in
/// URL query strings, never in headers, never in log scope. Structured
/// logging uses named properties only (no body-string interpolation).
/// Tests pin this (<c>ComgatePaymentProviderTests</c>).
/// </para>
///
/// <para>
/// <b>Resilience.</b> Calls flow through the shared
/// <c>ResiliencePipelineRegistry&lt;string&gt;</c> keyed by
/// <see cref="HttpClientName"/>. Retries 3× with exponential jittered
/// backoff (200ms base) on <see cref="HttpRequestException"/>,
/// <see cref="TaskCanceledException"/>, and 408 / 429 / 5xx. After
/// exhaustion the caller sees <see cref="BusinessErrorMessage.PaymentProviderUnavailable"/>
/// (<see cref="ErrorType.Transient"/>).
/// </para>
/// </summary>
public sealed class ComgatePaymentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ComgateOptions> options,
    ICountryConfigurationRepository countryConfigurations,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    ILogger<ComgatePaymentProvider> logger) : IPaymentProvider
{
    /// <summary>Named HttpClient registered by <c>AddMakablesClients</c>.</summary>
    public const string HttpClientName = "comgate";

    /// <summary>
    /// Provider code; matches <c>CountryConfiguration.DefaultPaymentProvider</c>
    /// for keyed-service selection.
    /// </summary>
    public const string ProviderCode = "comgate";

    /// <summary>Comgate-specific error codes the adapter recognises.</summary>
    private const string ComgateCodeOk = "0";
    /// <summary>Bad merchant id (per Comgate public docs).</summary>
    private const string ComgateCodeBadMerchant = "1100";
    /// <summary>Bad secret / auth (per Comgate public docs).</summary>
    private const string ComgateCodeBadSecret = "1102";

    public string Code => ProviderCode;

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    public async Task<BusinessResult<PaymentSession>> CreatePaymentAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var opts = options.Value;

        // Country config drives the per-country variation (lang, country).
        // Single global Comgate credential set (Q4); per-country merchant
        // ids land when SK/PL/HU launches need them.
        var config = await countryConfigurations.GetByCodeAsync(order.CountryCode, cancellationToken);
        if (config is null)
        {
            // Same shape as the factory's missing-country lookup so the
            // caller sees a uniform code regardless of which layer hit it.
            logger.LogWarning(
                "Comgate.CreatePayment: country configuration for {CountryCode} is missing.",
                order.CountryCode);
            return BusinessResult.Failure<PaymentSession>(
                Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
        }

        // Comgate's `lang` field is the 2-letter language code; we store
        // "cs-CZ" so take the prefix. Defensive on shorter values — the
        // CountryConfiguration validator already requires a non-empty code,
        // but we don't want a substring overrun on an admin typo either.
        var lang = config.DefaultLanguageCode.Length >= 2
            ? config.DefaultLanguageCode[..2].ToLowerInvariant()
            : config.DefaultLanguageCode.ToLowerInvariant();

        var formFields = new List<KeyValuePair<string, string>>
        {
            new("merchant", opts.MerchantId),
            new("price", order.TotalAmountMinor.ToString(CultureInfo.InvariantCulture)),
            new("curr", order.Currency),
            new("label", $"Objednávka {order.OrderNumber}"),
            new("refId", order.Id),
            new("email", order.ContactEmail),
            new("prepareOnly", "true"),
            new("country", order.CountryCode),
            new("lang", lang),
            new("method", "ALL"),
            // Secret stays at the end; never logged.
            new("secret", opts.Secret),
        };

        var url = $"{opts.BaseUrl.TrimEnd('/')}/v1.0/create";

        var callResult = await CallComgateAsync(
            url, formFields, opts, operationLabel: "create", cancellationToken);
        if (!callResult.IsSuccess)
        {
            return BusinessResult.Failure<PaymentSession>(callResult.Error!);
        }

        var fields = callResult.Value!;
        var code = GetField(fields, "code");
        if (code != ComgateCodeOk)
        {
            return MapComgateBusinessError<PaymentSession>(code, fields, "create");
        }

        var transId = GetField(fields, "transId");
        var redirect = GetField(fields, "redirect");
        if (string.IsNullOrWhiteSpace(transId) || string.IsNullOrWhiteSpace(redirect))
        {
            logger.LogError(
                "Comgate.CreatePayment: code=0 but transId or redirect missing (refId={RefId}).",
                order.Id);
            return BusinessResult.Failure<PaymentSession>(
                Error.Permanent(BusinessErrorMessage.PaymentUnknownError));
        }

        logger.LogInformation(
            "Comgate.CreatePayment: session created for order {OrderId} (transId={TransId}).",
            order.Id, transId);
        return BusinessResult.Success(new PaymentSession(transId, redirect));
    }

    public async Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(
        string providerRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerRef))
            throw new ArgumentException("ProviderRef is required.", nameof(providerRef));

        var opts = options.Value;
        var trimmed = providerRef.Trim();

        // GET with query-string form per ADR 0016 §"Webhook handling". The
        // secret IS in the query string per Comgate's documented status
        // endpoint shape; that's the wire contract and there is no header
        // alternative. The OTel HTTP instrumentation strips the secret via
        // the SensitivePropertyMasker (T-0031 sec B-1 precedent — the
        // masker pattern matches on the parameter name). LastRequest assertions
        // in the unit tests verify it never lands in any LogScope.
        var url = $"{opts.BaseUrl.TrimEnd('/')}/v1.0/status" +
                  $"?merchant={Uri.EscapeDataString(opts.MerchantId)}" +
                  $"&secret={Uri.EscapeDataString(opts.Secret)}" +
                  $"&transId={Uri.EscapeDataString(trimmed)}";

        var callResult = await CallComgateGetAsync(url, opts, operationLabel: "status", cancellationToken);
        if (!callResult.IsSuccess)
        {
            return BusinessResult.Failure<PaymentStatus>(callResult.Error!);
        }

        var fields = callResult.Value!;
        var code = GetField(fields, "code");
        if (code != ComgateCodeOk)
        {
            return MapComgateBusinessError<PaymentStatus>(code, fields, "status");
        }

        var status = GetField(fields, "status");
        var state = MapComgateStatus(status);
        if (state is null)
        {
            logger.LogWarning(
                "Comgate.VerifyPayment: unknown status '{Status}' for transId={TransId}.",
                status, trimmed);
            return BusinessResult.Failure<PaymentStatus>(
                Error.Unknown(BusinessErrorMessage.PaymentUnknownError));
        }

        DateTimeOffset? paidAt = null;
        var paymentTime = GetField(fields, "paymentTime");
        if (state == PaymentState.Paid && !string.IsNullOrWhiteSpace(paymentTime)
            && DateTimeOffset.TryParse(paymentTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            paidAt = parsed;
        }

        var method = GetField(fields, "method");
        var normalisedMethod = string.IsNullOrWhiteSpace(method) ? null : method;

        return BusinessResult.Success(new PaymentStatus(state.Value, normalisedMethod, paidAt));
    }

    public Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(
        HttpRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "ParseAndVerifyWebhookAsync is implemented in T-0066. " +
            "If you are reading this, the webhook handler in T-0066 has not yet shipped.");

    public Task<BusinessResult<RefundReceipt>> RefundAsync(
        string providerRef, long amountMinor, string currency, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "RefundAsync is implemented in T-0105. " +
            "If you are reading this, the admin refund command in T-0105 has not yet shipped.");

    // ---- helpers ----

    /// <summary>
    /// Run a form-urlencoded POST through the resilience pipeline and parse
    /// the response body (also form-urlencoded). Returns the parsed fields
    /// on HTTP-2xx, or a classified <see cref="Error"/> on any failure
    /// (network, timeout, 5xx, parse failure). The body is read with the
    /// cancellation token linked to the overall request — same pattern as
    /// the ARES adapter.
    /// </summary>
    private async Task<BusinessResult<IDictionary<string, StringValues>>> CallComgateAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> formFields,
        ComgateOptions opts,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(opts.MerchantId) || string.IsNullOrWhiteSpace(opts.Secret))
        {
            // ValidateOnStart should have caught this at boot; defensive.
            logger.LogError("Comgate options are not configured (merchant or secret missing).");
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    // Fresh request per attempt — Polly retries by re-running
                    // the delegate, and a sent HttpRequestMessage can't be
                    // resent. The form content is rebuilt inside so the
                    // disposed stream from a previous attempt is irrelevant.
                    var content = new FormUrlEncodedContent(formFields);
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
            logger.LogWarning(ex, "Comgate.{Operation}: HTTP error after retries.", operationLabel);
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Comgate.{Operation}: timeout after retries.", operationLabel);
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable));
        }

        return await ReadResponseAsync(response, operationLabel, cancellationToken);
    }

    private async Task<BusinessResult<IDictionary<string, StringValues>>> CallComgateGetAsync(
        string url,
        ComgateOptions opts,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(opts.MerchantId) || string.IsNullOrWhiteSpace(opts.Secret))
        {
            logger.LogError("Comgate options are not configured (merchant or secret missing).");
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
            logger.LogWarning(ex, "Comgate.{Operation}: HTTP error after retries.", operationLabel);
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable));
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Comgate.{Operation}: timeout after retries.", operationLabel);
            return BusinessResult.Failure<IDictionary<string, StringValues>>(
                Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable));
        }

        return await ReadResponseAsync(response, operationLabel, cancellationToken);
    }

    private async Task<BusinessResult<IDictionary<string, StringValues>>> ReadResponseAsync(
        HttpResponseMessage response,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var isTransient = status is 408 or 429 or >= 500 and <= 599;
                logger.LogWarning(
                    "Comgate.{Operation}: HTTP {Status} after retries.",
                    operationLabel, status);
                return BusinessResult.Failure<IDictionary<string, StringValues>>(isTransient
                    ? Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable)
                    : Error.Permanent(BusinessErrorMessage.PaymentProviderRejected));
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Comgate.{Operation}: failed to read response body.", operationLabel);
                return BusinessResult.Failure<IDictionary<string, StringValues>>(
                    Error.Transient(BusinessErrorMessage.PaymentProviderUnavailable));
            }

            // Form-urlencoded response per ADR 0016 §93. QueryHelpers.ParseQuery
            // is the canonical parser; tolerant to missing fields (returns an
            // empty StringValues for any key the caller asks for).
            var parsed = QueryHelpers.ParseQuery(body);
            return BusinessResult.Success<IDictionary<string, StringValues>>(parsed);
        }
    }

    private BusinessResult<T> MapComgateBusinessError<T>(
        string code,
        IDictionary<string, StringValues> fields,
        string operationLabel)
    {
        var message = GetField(fields, "message");

        // Bad merchant / bad secret → Configuration + Critical (alert ops).
        if (code is ComgateCodeBadMerchant or ComgateCodeBadSecret)
        {
            logger.LogCritical(
                "Comgate.{Operation}: misconfigured credentials (Comgate code={ComgateCode}, message={ComgateMessage}).",
                operationLabel, code, message);
            return BusinessResult.Failure<T>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured));
        }

        // Empty `code` field — provider returned a body without a status code.
        // Treat as Unknown so the retry policy bites at the cap (Mapbox/Ares
        // precedent: 3 attempts via the Polly pipeline).
        if (string.IsNullOrEmpty(code))
        {
            logger.LogError(
                "Comgate.{Operation}: response missing 'code' field (message={ComgateMessage}).",
                operationLabel, message);
            return BusinessResult.Failure<T>(
                Error.Unknown(BusinessErrorMessage.PaymentUnknownError));
        }

        // Everything else: a known business rejection (invalid amount,
        // declined card on prepareOnly, etc.) — Permanent.
        logger.LogError(
            "Comgate.{Operation}: provider rejected request (Comgate code={ComgateCode}, message={ComgateMessage}).",
            operationLabel, code, message);
        return BusinessResult.Failure<T>(
            Error.Permanent(BusinessErrorMessage.PaymentProviderRejected));
    }

    private static PaymentState? MapComgateStatus(string status) =>
        status switch
        {
            "PENDING" => PaymentState.Pending,
            "AUTHORIZED" => PaymentState.Authorized,
            "PAID" => PaymentState.Paid,
            "CANCELLED" => PaymentState.Cancelled,
            _ => null,
        };

    private static string GetField(IDictionary<string, StringValues> fields, string key) =>
        fields.TryGetValue(key, out var values) ? values.ToString() : string.Empty;
}
