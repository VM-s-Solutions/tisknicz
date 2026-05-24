namespace Makables.Infra.Clients.SendGrid;

/// <summary>
/// SendGrid adapter configuration per ADR 0019 (amended for SendGrid
/// Dynamic Templates + DB-backed translation). Bound from <c>SendGrid</c>
/// in configuration; secrets injected via Key Vault references at deploy
/// time per ADR 0016.
/// </summary>
public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    /// <summary>SendGrid API key (Key Vault reference in prod).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default from-address used when an <c>EmailTemplate</c> doesn't
    /// override it (e.g. <c>"objednavky@makables.cz"</c>).
    /// </summary>
    public string DefaultFromAddress { get; set; } = "no-reply@makables.cz";

    /// <summary>Default from display name.</summary>
    public string DefaultFromName { get; set; } = "Makables";

    /// <summary>
    /// Polly retry policy parameters. SendGrid is the upstream — short,
    /// few retries because the outbox processor will pick up failed rows
    /// again on the next tick. Default of 1 is intentional (T-0028 sec
    /// reviewer M-4): outbox-level retry is the authoritative budget per
    /// ADR 0019; the in-provider retry handles a single transient blip
    /// without forcing the outbox row back into the queue.
    /// </summary>
    public int RetryCount { get; set; } = 1;
    public int RetryBaseDelayMs { get; set; } = 300;

    /// <summary>
    /// Hard upper bound on a single <c>SendEmailAsync</c> attempt (including
    /// retries). Protects against a stuck connection pinning an outbox-processor
    /// worker. T-0028 sec reviewer M-4.
    /// </summary>
    public int PerSendTimeoutSeconds { get; set; } = 10;
}
