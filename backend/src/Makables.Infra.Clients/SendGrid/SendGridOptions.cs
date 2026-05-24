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
    /// again on the next tick.
    /// </summary>
    public int RetryCount { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 300;
}
