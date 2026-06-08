namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "your order has shipped" email,
/// enqueued by both the Zásilkovna <c>ShipOrder.Handler</c> (T-0072) and
/// the personal-pickup <c>HandOverOrder.Handler</c> (T-0073). Unified
/// payload + event + SendGrid template per T-0073 locked decision A.1.
///
/// <para>
/// <see cref="TrackingUrl"/> is nullable — Zásilkovna always sets it to
/// <c>https://tracking.packeta.com/Z{carrierRef}</c>; personal-pickup
/// always passes null (no carrier, no tracking). The SendGrid template
/// conditionally renders the tracking-URL row only when the substitution
/// is non-empty, so a single template covers both flows.
/// </para>
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// (no <c>PropertyNamingPolicy</c>) round-trips every payload shape.
/// </para>
///
/// <para>
/// <see cref="LanguageCode"/> is resolved at enqueue time (T-0028 pattern);
/// <see cref="ActionUrl"/> is pre-baked
/// (<c>{WebBaseUrl}/objednavka/{OrderId}</c>) per the T-0067 producer-side
/// URL convention. <b>No invoice attachment</b> — the label is for the
/// maker, customer gets the tracking link instead. T-0072 locked decision.
/// </para>
/// </summary>
public sealed record OrderShippedCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    string LanguageCode,
    string ActionUrl,
    string? TrackingUrl);
