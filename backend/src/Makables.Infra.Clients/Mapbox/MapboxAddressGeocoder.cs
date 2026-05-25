using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

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
/// All failures are surfaced as <see cref="BusinessResult{T}"/> failures
/// (no exceptions cross the boundary). Per ADR 0010 §"Geocoding policy"
/// callers ignore non-blocking failures: a maker-reg handler leaves
/// lat/lng null on transient failure and the address gets re-geocoded
/// later via the partial index sweep.
/// </summary>
public sealed class MapboxAddressGeocoder(
    IHttpClientFactory httpClientFactory,
    ResiliencePipeline<HttpResponseMessage> retryPipeline,
    IOptions<MapboxOptions> options,
    ILogger<MapboxAddressGeocoder> logger) : IAddressGeocoder
{
    /// <summary>Named HttpClient registered by the wiring extension.</summary>
    public const string HttpClientName = "Makables.Infra.Clients.Mapbox";

    public async Task<BusinessResult<Coordinates>> GeocodeAsync(
        Address address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var query = $"{address.Street} {address.HouseNumber}, {address.Zip} {address.City}";
        var opts = options.Value;
        var url = BuildUrl(opts, query, address.CountryCodeIso, autocomplete: false, limit: 1);

        var (response, error) = await CallMapboxAsync(url, "geocode", cancellationToken);
        if (response is null) return BusinessResult.Failure<Coordinates>(error!);

        using (response)
        {
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
    }

    public async Task<BusinessResult<IReadOnlyList<AddressSuggestion>>> AutocompleteAsync(
        string query,
        string countryCodeIso,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(
                Error.Validation("query", BusinessErrorMessage.GeocoderInvalidInput));
        if (string.IsNullOrWhiteSpace(countryCodeIso) || countryCodeIso.Length != 2)
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(
                Error.Validation("countryCodeIso", BusinessErrorMessage.GeocoderInvalidInput));

        var opts = options.Value;
        var url = BuildUrl(opts, query, countryCodeIso, autocomplete: true, limit: opts.AutocompleteLimit);

        var (response, error) = await CallMapboxAsync(url, "autocomplete", cancellationToken);
        if (response is null)
            return BusinessResult.Failure<IReadOnlyList<AddressSuggestion>>(error!);

        using (response)
        {
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
    }

    // ---- helpers ----

    private async Task<(HttpResponseMessage? Response, Error? Error)> CallMapboxAsync(
        string url, string operationLabel, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.AccessToken))
        {
            logger.LogError("Mapbox:AccessToken is not configured.");
            return (null, Error.Configuration(BusinessErrorMessage.GeocoderPermanentFailure));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, opts.PerCallTimeoutSeconds)));

        HttpResponseMessage response;
        try
        {
            response = await retryPipeline.ExecuteAsync(
                async ct => await http.GetAsync(url, ct),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Mapbox {Operation} timed out after {Seconds}s.",
                operationLabel, opts.PerCallTimeoutSeconds);
            return (null, Error.Transient(BusinessErrorMessage.GeocoderTransientFailure));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mapbox {Operation} threw after retries.", operationLabel);
            return (null, Error.Transient(BusinessErrorMessage.GeocoderTransientFailure));
        }

        if (response.IsSuccessStatusCode) return (response, null);

        var status = (int)response.StatusCode;
        var isTransient = status is 408 or 429 or >= 500 and <= 599;
        logger.LogWarning("Mapbox {Operation} returned {Status}.", operationLabel, status);
        response.Dispose();
        return (null, isTransient
            ? Error.Transient(BusinessErrorMessage.GeocoderTransientFailure)
            : Error.Permanent(BusinessErrorMessage.GeocoderPermanentFailure));
    }

    private static string BuildUrl(MapboxOptions opts, string query, string country, bool autocomplete, int limit)
    {
        // Mapbox v5 Geocoding format:
        //   /geocoding/v5/mapbox.places/{encoded-query}.json?...
        var encoded = Uri.EscapeDataString(query);
        var qs = new[]
        {
            $"country={Uri.EscapeDataString(country.ToLowerInvariant())}",
            $"autocomplete={(autocomplete ? "true" : "false")}",
            $"limit={Math.Clamp(limit, 1, 10)}",
            // language=cs is reasonable Czech-launch default; we'd plumb a
            // per-request override here if/when the frontend asks for it.
            "language=cs",
            $"access_token={Uri.EscapeDataString(opts.AccessToken)}",
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
