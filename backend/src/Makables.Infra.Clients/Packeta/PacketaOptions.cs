namespace Makables.Infra.Clients.Packeta;

/// <summary>
/// Configuration for the Packeta carrier adapter (T-0070). Bound from
/// the <c>Packeta</c> section of configuration. Both keys are Key Vault
/// references in production; dev environments use the sandbox values.
/// Validated at startup by <see cref="PacketaOptionsValidator"/>.
/// </summary>
public sealed class PacketaOptions
{
    public const string SectionName = "Packeta";

    /// <summary>
    /// Private API key — Packeta's <c>apiPassword</c>, sent as a form field
    /// on every REST call. NEVER logged. Required.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Public widget key — the value the frontend Packeta v6 widget loads
    /// client-side to talk to Packeta. Public-but-secret-from-spam (committing
    /// it gives bots a fixed target). Required.
    /// </summary>
    public string PublicWidgetKey { get; set; } = string.Empty;

    /// <summary>
    /// Sender label for the platform's Packeta account (e.g. <c>"makables-cz"</c>).
    /// Platform-wide at MVP; per-maker Phase 5+. Required.
    /// </summary>
    public string SenderLabel { get; set; } = "makables-cz";

    /// <summary>
    /// Packeta REST API base URL. Default is the production endpoint;
    /// dev environments override to the sandbox.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.packeta.com";

    /// <summary>
    /// Packeta v6 widget script URL the frontend embeds on the checkout
    /// page. Returned to the public widget-config endpoint.
    /// </summary>
    public string WidgetScriptUrl { get; set; } = "https://widget.packeta.com/v6/www/js/library.js";

    /// <summary>
    /// True when the adapter should target Packeta's sandbox. Default
    /// <c>false</c> — a misconfigured production deploy that defaulted
    /// to <c>true</c> would silently route all shipments to sandbox.
    /// Production-default = false forces explicit override in dev.
    /// </summary>
    public bool TestMode { get; set; }
}
