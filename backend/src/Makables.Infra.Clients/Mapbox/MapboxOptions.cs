namespace Makables.Infra.Clients.Mapbox;

/// <summary>
/// Mapbox Geocoding API config per ADR 0010 §"Mapbox autocomplete +
/// geocoding". Bound from <c>Mapbox</c> section of configuration; the
/// access token is a Key Vault reference in prod.
///
/// T-0031 uses a SINGLE server-side token. The frontend never calls
/// Mapbox directly — the autocomplete proxy endpoint on Customer +
/// Maker hosts is the only path. Keeps per-user rate limiting honest
/// (the user can't bypass our limit by hitting Mapbox directly).
/// </summary>
public sealed class MapboxOptions
{
    public const string SectionName = "Mapbox";

    /// <summary>
    /// Mapbox access token. Either a public <c>pk.</c> or secret <c>sk.</c>
    /// token works for the Geocoding API; a domain-restricted public
    /// token is acceptable because the token never leaves the backend.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the Mapbox API. Defaults to the production endpoint;
    /// override in tests to point at a stub.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.mapbox.com";

    /// <summary>
    /// Maximum autocomplete suggestions returned per query (Mapbox supports
    /// 1..10; default 5 keeps the UI tidy and trims Mapbox cost).
    /// </summary>
    public int AutocompleteLimit { get; set; } = 5;

    /// <summary>
    /// Polly retry count for both Geocode and Autocomplete. Short — Mapbox
    /// is usually reliable; outbox-style background retry is owned by a
    /// future sweep job per ADR 0010 §"Geocoding policy".
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Polly retry base delay (ms). Exponential w/ jitter from here.</summary>
    public int RetryBaseDelayMs { get; set; } = 200;

    /// <summary>
    /// Wall-clock upper bound on a Mapbox operation INCLUDING all Polly
    /// retries. Renamed from <c>PerCallTimeoutSeconds</c> in T-0031 sec
    /// reviewer follow-up — the linked <c>CancellationTokenSource</c>
    /// bounds the whole retry chain, not a single HTTP attempt.
    /// </summary>
    public int OverallTimeoutSeconds { get; set; } = 5;
}
