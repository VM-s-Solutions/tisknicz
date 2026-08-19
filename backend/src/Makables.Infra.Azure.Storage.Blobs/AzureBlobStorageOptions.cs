namespace Makables.Infra.Azure.Storage.Blobs;

/// <summary>
/// Configuration for <see cref="AzureBlobStorageClient"/>. Two
/// authentication modes:
///
/// <list type="bullet">
///   <item><description><b>Connection string</b> — set <see cref="ConnectionString"/>
///     for local dev (Azurite emulator) and CI. Highest priority.</description></item>
///   <item><description><b>Managed Identity</b> — set <see cref="ServiceUri"/> only
///     (e.g. <c>https://makablesprod.blob.core.windows.net</c>); the
///     adapter uses <c>DefaultAzureCredential</c>. The App Service
///     identity must have the <c>Storage Blob Data Contributor</c>
///     role on the account.</description></item>
/// </list>
///
/// At least one of the two MUST be configured. <c>ValidateOnStart</c>
/// catches a missing config at boot.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    // NOT "AzureBlobStorage": Azure Functions/App Service reserves that prefix
    // for its storage-binding connection convention and rejects any app setting
    // named AzureBlobStorage__* (error 04072). "BlobStorage" is safe and maps to
    // the BlobStorage__ConnectionString app setting the Bicep injects.
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Azure storage connection string. Local dev only — points at
    /// Azurite when <c>UseDevelopmentStorage=true</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Storage account endpoint, e.g. <c>https://makablesprod.blob.core.windows.net</c>.
    /// Used with <c>DefaultAzureCredential</c> in staging / production.
    /// </summary>
    public string? ServiceUri { get; set; }

    /// <summary>
    /// Maximum SDK retry attempts after the first try. The Azure default
    /// is 5, which combined with the default 60 s <c>MaxDelay</c> lets a
    /// dead storage endpoint sleep ~25 s of pure backoff before the call
    /// fails (observed: a 26.4 s POST /me/maker/logo when Azurite was
    /// down). Bounded here so a storage outage surfaces as a fast
    /// <c>blob.*Failed</c> transient instead of a hung request.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial retry backoff. Exponential mode adds jitter (CLAUDE.md §5).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Ceiling for a single backoff interval. With <see cref="MaxRetries"/>
    /// = 3 the total time spent sleeping is at most ~3.25 s.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Per-HTTP-request timeout (the Azure default is 100 s). Caps the
    /// hung-socket case that the retry ceiling alone cannot: a stalled
    /// TCP connection would otherwise burn
    /// <c>(MaxRetries + 1) × 100 s</c> = 400 s before the caller learns
    /// anything.
    /// <para>
    /// 20 s keeps the worst case at
    /// <c>4 × 20 s + ~3.25 s backoff ≈ 83 s</c>, inside the frontend's
    /// 120 s <c>UPLOAD_TIMEOUT_MS</c> — so a storage outage reaches the
    /// user as a localised <c>blob.*Failed</c> message instead of a bare
    /// client-side abort. It is still ~40× the time the largest upload
    /// we accept (a 10 MiB order attachment) needs on the intra-region
    /// App Service → Storage hop.
    /// </para>
    /// </summary>
    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromSeconds(20);
}
