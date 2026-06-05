namespace Makables.Infra.Clients.Comgate;

/// <summary>
/// Comgate payment gateway config per ADR 0016. Single global section
/// (user decision Q4): per-country variation (<c>lang</c>, <c>country</c>,
/// <c>prepareOnly</c>) is read from <c>CountryConfiguration</c> at call time.
///
/// <para>
/// <see cref="WebhookAllowedIps"/> lives here even though T-0066 owns the
/// webhook — registering it on T-0065 keeps T-0066's DI footprint to zero.
/// </para>
/// </summary>
public sealed class ComgateOptions
{
    public const string SectionName = "Comgate";

    /// <summary>Comgate merchant id (numeric, but stored as string).</summary>
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>
    /// Shared secret used for the body <c>secret</c> field and the webhook
    /// signature. NEVER appears in logs, NEVER in URLs.
    /// </summary>
    public string Secret { get; init; } = string.Empty;

    /// <summary>Base URL of the Comgate API. Production default.</summary>
    public string BaseUrl { get; init; } = "https://payments.comgate.cz";

    /// <summary>
    /// True for Comgate's sandbox. Today this is only a label — the
    /// switching is via <see cref="BaseUrl"/>. Kept here for the future
    /// admin UI / observability.
    /// </summary>
    public bool TestMode { get; init; }

    /// <summary>
    /// IP allowlist for the T-0066 webhook endpoint. Empty list at MVP
    /// (lookup defers to T-0066's implementation); operators populate via
    /// configuration before going live.
    /// </summary>
    public IReadOnlyList<string> WebhookAllowedIps { get; init; } = Array.Empty<string>();
}
