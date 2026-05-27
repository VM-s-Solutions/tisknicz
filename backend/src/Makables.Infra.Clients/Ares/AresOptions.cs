namespace Makables.Infra.Clients.Ares;

/// <summary>
/// ARES (Czech state company registry) client config per ADR 0018.
/// ARES is a public registry — no API key. The token-leak posture from
/// T-0031 doesn't apply here (no secret in URL), but we still:
///   - require absolute https on <see cref="BaseUrl"/>,
///   - cap the overall retry-chain timeout,
///   - keep the retry count small (ARES is usually fast but occasionally
///     slow per ADR 0018; the DB cache + 7-day stale-fallback are the
///     authoritative durability primitives).
/// </summary>
public sealed class AresOptions
{
    public const string SectionName = "Ares";

    /// <summary>
    /// Base URL of the ARES REST API. Defaults to the production
    /// endpoint; integration tests inject a stub host.
    /// </summary>
    public string BaseUrl { get; set; } = "https://ares.gov.cz";

    /// <summary>Polly retry count for ARES calls. Short — see class doc.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Polly retry base delay (ms). Exponential w/ jitter from here.</summary>
    public int RetryBaseDelayMs { get; set; } = 300;

    /// <summary>Wall-clock upper bound on one ARES operation INCLUDING all retries.</summary>
    public int OverallTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How long an in-memory cache entry stays hot. ADR 0018: 1 hour
    /// covers the maker-mid-registration retry case without HTTP.
    /// </summary>
    public int InMemoryCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// How long a DB-cache row stays "fresh" before a normal lookup
    /// would re-fetch. ADR 0018: 24 hours.
    /// </summary>
    public int DbCacheTtlHours { get; set; } = 24;

    /// <summary>
    /// How far past <c>expires_at</c> a DB row can be served as a stale
    /// fallback when ARES is unreachable. ADR 0018: 7 days.
    /// </summary>
    public int StaleFallbackDays { get; set; } = 7;
}
