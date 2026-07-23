namespace Makables.Infra.Clients.Resend;

/// <summary>
/// Resend adapter configuration per ADR 0019 (re-amended to Resend,
/// T-0157 — the processor list confirmed at the 2026-07-04 meeting names
/// Resend, and the operator directed the switch). Bound from
/// <c>Resend</c> in configuration; the API key arrives as a Key Vault
/// reference at deploy time.
/// </summary>
public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    /// <summary>Resend API key (<c>re_…</c>; Key Vault reference in deployed envs).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Resend REST endpoint. Overridable for tests; never changes in
    /// real environments.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.resend.com";

    /// <summary>
    /// Default from-address used when an <c>EmailTemplate</c> doesn't
    /// override it. MUST belong to a domain verified in Resend
    /// (<c>makables.cz</c>) — Resend rejects unverified senders; dev can
    /// use <c>onboarding@resend.dev</c> before the domain is verified.
    /// </summary>
    public string DefaultFromAddress { get; set; } = "no-reply@makables.cz";

    /// <summary>Default from display name.</summary>
    public string DefaultFromName { get; set; } = "Makables";

    /// <summary>
    /// Polly retry parameters — same philosophy as the SendGrid adapter
    /// (T-0028 sec reviewer M-4): the outbox processor owns the
    /// authoritative retry budget per ADR 0019; in-provider retry only
    /// rides out a single transient blip.
    /// </summary>
    public int RetryCount { get; set; } = 1;
    public int RetryBaseDelayMs { get; set; } = 300;

    /// <summary>Hard upper bound on a single send attempt including retries.</summary>
    public int PerSendTimeoutSeconds { get; set; } = 10;
}
