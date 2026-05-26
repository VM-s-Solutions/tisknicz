using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace Makables.Infra.Clients.Mapbox;

/// <summary>
/// Mapbox Geocoding API (v5) adapter per ADR 0010. Two endpoints:
///
/// <list type="bullet">
///   <item><description>Forward geocode — structured address → coordinates.</description></item>
///   <item><description>Autocomplete — partial query → suggestions array.</description></item>
/// </list>
///
/// Both use the same Mapbox URL family
/// (<c>{BaseUrl}/geocoding/v5/mapbox.places/{query}.json</c>); the
/// difference is only in the query-string flags. Calls go through a
/// named <see cref="IHttpClientFactory"/> HttpClient + a shared Polly
/// <see cref="ResiliencePipeline{HttpResponseMessage}"/> registered in
/// <c>AddMakablesClients</c>.
///
/// <b>Token transport (T-0031 sec reviewer B-1).</b> The Mapbox access
/// token is sent as an <c>Authorization: Bearer</c> header per request,
/// NOT in the URL query string. OTel HttpClient instrumentation (ADR
/// 0023) captures every request URL into <c>url.full</c> span attributes
/// that ship to Application Insights — a token in the query string
/// would leak verbatim to anyone with App Insights read access. The
/// Authorization header is stripped from OTel HTTP span attributes by
/// default AND is on the <c>SensitivePropertyMasker</c> redaction list
/// for Serilog.
///
/// All failures are surfaced as <see cref="BusinessResult{T}"/> failures
/// (no exceptions cross the boundary). Per ADR 0010 §"Geocoding policy"
/// callers ignore non-blocking failures: a maker-reg handler leaves
/// lat/lng null on transient failure and the address gets re-geocoded
/// later via the partial index sweep.
/// </summary>
public sealed class MapboxAddressGeocoder(
    IHttpClientFactory httpClientFactory,
    ResiliencePipelineRegistry<string> pipelineRegistry,
    IOptions<MapboxOptions> options,
    ILogger<MapboxAddressGeocoder> logger) : IAddressGeocoder
{
    /// <summary>Named HttpClient registered by the wiring extension.</summary>
    public const string HttpClientName = "Makables.Infra.Clients.Mapbox";

    private ResiliencePipeline<HttpResponseMessage> RetryPipeline =>
        pipelineRegistry.GetPipeline<HttpResponseMessage>(HttpClientName);

    public async Task<BusinessResult<Coordinates>> GeocodeAsync(
        Address address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var query = $"{address.Street} {address.HouseNumber}, {address.Zip} {address.City}";
        var opts = options.Value;
        var url = BuildUrl(opts, query, address.CountryCodeIso, autocomplete: false, limit: 1);

        var callResult = await CallMapboxAsync(url, opts, "geocode", cancellationToken);
        if (!callResult.IsSuccess)
            return BusinessResult.Failure<Coordinates>(callResult.Error!);

        using var response = callResult.Value!;
        MapboxFeatureCollection? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<MapboxFeatureCollection>(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mapbox geocode response could not be deserialised.");
            return BusinessResult.Failure<Coordinates>(
                Error.Permanent(BusinessErrorMessage.GeocoderPermanentFailure));
        }

        var first = payload?.Features?.FirstOrDefault();
        if (first is null || first.Center is null || first.Center.Length < 2)
        {
            logger.LogInformation("Mapbox geocode returned no match for address id {AddressId}.", address.Id);
            return BusinessResult.Failure<Coordinates>(
                Error.Permanent(BusinessErrorMessage.GeocoderNoMatch));
        }

        // Mapbox returns [longitude, latitude] — note the order.
        try
        {
            return BusinessResult.Success(Coordinates.Of(first.Center[1], first.Center[0]));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            logger.LogWarning(ex, "Mapbox returned out-of-range coordinates for address id {AddressId}.", address.Id);
            return BusinessResult.Failure<Coordinates>(
                Error.Permanent(BusinessErrorMessage.GeocoderPermanentFailure));
        }
    }

    public async Task<BusinessResult<IReadOnlyList<AddressSuggestion>>> AutocompleteAsync(
        string query,
        string countryCodeIso,
        CancellationToken cancellationToken)
    {
        if (!AutocompleteInputGuard.IsValid(query, countryCodeIso, out var field))
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(
                Error.Validation(field!, BusinessErrorMessage.GeocoderInvalidInput));

        var opts = options.Value;
        var url = BuildUrl(opts, query, countryCodeIso, autocomplete: true, limit: opts.AutocompleteLimit);

        var callResult = await CallMapboxAsync(url, opts, "autocomplete", cancellationToken);
        if (!callResult.IsSuccess)
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(callResult.Error!);

        using var response = callResult.Value!;
        MapboxFeatureCollection? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<MapboxFeatureCollection>(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mapbox autocomplete response could not be deserialised.");
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(
                Error.Permanent(BusinessErrorMessage.GeocoderPermanentFailure));
        }

        var suggestions = (payload?.Features ?? [])
            .Select(f => ToSuggestion(f, countryCodeIso))
            .ToList();
        return BusinessResult.Success<IReadOnlyList<AddressSuggestion>>(suggestions);
    }

    // ---- helpers ----

    /// <summary>
    /// One Mapbox operation wrapped in the Polly retry pipeline + the
    /// linked overall-timeout CTS. Returns the response on success
    /// (caller owns the dispose), or a classified <see cref="Error"/>
    /// inside a <see cref="BusinessResult{T}"/> failure on any failure
    /// path (T-0031 CQ reviewer M-2 — replaces the old
    /// <c>(HttpResponseMessage?, Error?)</c> tuple).
    /// </summary>
    private async Task<BusinessResult<HttpResponseMessage>> CallMapboxAsync(
        string url, MapboxOptions opts, string operationLabel, CancellationToken cancellationToken)
    {
        // ValidateOnStart already guarantees a non-blank token; this
        // boundary check is a defensive belt-and-braces, cheap to keep.
        if (string.IsNullOrWhiteSpace(opts.AccessToken))
        {
            logger.LogError("Mapbox:AccessToken is not configured.");
            return BusinessResult.Failure<HttpResponseMessage>(
                Error.Configuration(BusinessErrorMessage.GeocoderPermanentFailure));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.OverallTimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await RetryPipeline.ExecuteAsync(
                async ct =>
                {
                    // Build a fresh HttpRequestMessage per attempt — a request
                    // can only be sent once, and the Polly retry loop resends.
                    // Attaching the Authorization here keeps the token out of
                    // the URL (T-0031 sec B-1).
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
                    return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Mapbox {Operation} timed out after {Seconds}s.",
                operationLabel, opts.OverallTimeoutSeconds);
            return BusinessResult.Failure<HttpResponseMessage>(
                Error.Transient(BusinessErrorMessage.GeocoderTransientFailure));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mapbox {Operation} threw after retries.", operationLabel);
            return BusinessResult.Failure<HttpResponseMessage>(
                Error.Transient(BusinessErrorMessage.GeocoderTransientFailure));
        }

        if (response.IsSuccessStatusCode) return BusinessResult.Success(response);

        var status = (int)response.StatusCode;
        var isTransient = status is 408 or 429 or >= 500 and <= 599;
        logger.LogWarning("Mapbox {Operation} returned {Status}.", operationLabel, status);
        response.Dispose();
        return BusinessResult.Failure<HttpResponseMessage>(isTransient
            ? Error.Transient(BusinessErrorMessage.GeocoderTransientFailure)
            : Error.Permanent(BusinessErrorMessage.GeocoderPermanentFailure));
    }

    /// <summary>
    /// Builds the Mapbox URL <em>without</em> the access token (sent as
    /// the Authorization header instead, see class XML doc).
    /// <c>AutocompleteLimit</c> range is validated at startup so no
    /// runtime clamp is needed here.
    /// </summary>
    private static string BuildUrl(MapboxOptions opts, string query, string country, bool autocomplete, int limit)
    {
        var encoded = Uri.EscapeDataString(query);
        var qs = new[]
        {
            $"country={Uri.EscapeDataString(country.ToLowerInvariant())}",
            $"autocomplete={(autocomplete ? "true" : "false")}",
            $"limit={limit}",
            // language=cs is the Czech-launch default; a per-request
            // override lands when SK/PL/DE markets do.
            "language=cs",
        };
        return $"{opts.BaseUrl.TrimEnd('/')}/geocoding/v5/mapbox.places/{encoded}.json?{string.Join("&", qs)}";
    }

    private static AddressSuggestion ToSuggestion(MapboxFeature f, string fallbackCountry)
    {
        // Mapbox returns components in two places: top-level `text` /
        // `address` (the primary token), and `context[]` (an array of
        // parent entities labelled by id-prefix: postcode., place.,
        // region., country.). We project both into the structured
        // shape the frontend's address form binds to.
        //
        // Missing fields surface as empty strings rather than null
        // (T-0031 CQ reviewer N-3): the suggestion is a wire DTO bound
        // to a form, and `value={s.street ?? ''}` ceremony in TS is
        // worth avoiding by normalising server-side.
        var contexts = f.Context ?? [];
        var postcode = contexts.FirstOrDefault(c => c.Id?.StartsWith("postcode", StringComparison.Ordinal) == true)?.Text ?? string.Empty;
        var city = contexts.FirstOrDefault(c => c.Id?.StartsWith("place", StringComparison.Ordinal) == true)?.Text ?? string.Empty;
        var countryShort = contexts.FirstOrDefault(c => c.Id?.StartsWith("country", StringComparison.Ordinal) == true)
            ?.ShortCode?.ToUpperInvariant() ?? fallbackCountry.ToUpperInvariant();

        Coordinates? coords = null;
        if (f.Center is { Length: >= 2 } center)
        {
            try { coords = Coordinates.Of(center[1], center[0]); }
            catch (ArgumentOutOfRangeException) { coords = null; }
        }

        return new AddressSuggestion(
            Label: f.PlaceName ?? string.Empty,
            Street: f.Text ?? string.Empty,
            HouseNumber: f.Address ?? string.Empty,
            City: city,
            Zip: postcode,
            CountryCodeIso: countryShort,
            Coordinates: coords);
    }

    // ---- Mapbox response shape ----

    private sealed record MapboxFeatureCollection(
        [property: JsonPropertyName("features")] MapboxFeature[]? Features);

    private sealed record MapboxFeature(
        [property: JsonPropertyName("place_name")] string? PlaceName,
        [property: JsonPropertyName("text")]       string? Text,
        [property: JsonPropertyName("address")]    string? Address,
        [property: JsonPropertyName("center")]     double[]? Center,
        [property: JsonPropertyName("context")]    MapboxContext[]? Context);

    private sealed record MapboxContext(
        [property: JsonPropertyName("id")]         string? Id,
        [property: JsonPropertyName("text")]       string? Text,
        [property: JsonPropertyName("short_code")] string? ShortCode);
}

/// <summary>
/// Shared input guard for autocomplete requests. Lives next to the
/// adapter that uses it (T-0031 CQ reviewer M-4) so a future
/// MediatR-command-style controller can call the same guard without
/// re-implementing the rules. Today the adapter is the only caller;
/// promoting to a shared <c>Core.AppServices</c> mixin lands with the
/// first feature that actually needs both an adapter call AND an
/// HTTP-layer validation pass.
/// </summary>
internal static class AutocompleteInputGuard
{
    public static bool IsValid(string? query, string? countryCodeIso, out string? failedField)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            failedField = "query";
            return false;
        }
        if (string.IsNullOrWhiteSpace(countryCodeIso) || countryCodeIso.Length != 2)
        {
            failedField = "countryCodeIso";
            return false;
        }
        failedField = null;
        return true;
    }
}
