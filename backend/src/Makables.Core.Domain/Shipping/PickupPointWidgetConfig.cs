namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Configuration the customer-facing checkout widget needs to render the
/// carrier's pickup-point picker (Packeta v6 widget at MVP). Returned by
/// <see cref="IShippingCarrier.WidgetConfig"/> and surfaced via the
/// public widget-config endpoint (T-0070).
///
/// <para>
/// <see cref="Options"/> is a loose dictionary — the Packeta widget's
/// v6→v7 surface may add new config keys, so a strongly-typed shape
/// would force a breaking change every time. <see cref="ScriptUrl"/>
/// + <see cref="PublicKey"/> are the two non-negotiable fields the
/// frontend always reads. Per T-0070 ADR-locked decision B.
/// </para>
/// </summary>
public sealed record PickupPointWidgetConfig(
    string ScriptUrl,
    string PublicKey,
    IReadOnlyDictionary<string, string> Options);
