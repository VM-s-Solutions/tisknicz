namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "your maker accepted the order"
/// notification email, enqueued by <c>AcceptOrder.Handler</c> after the
/// Paid → Accepted state transition. T-0071 (US-maker-0006).
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// (no <c>PropertyNamingPolicy</c>) round-trips both shapes.
/// </para>
///
/// <para>
/// <see cref="LanguageCode"/> is resolved at enqueue time per the T-0028
/// pattern. <see cref="ActionUrl"/> is pre-baked
/// (<c>{WebBaseUrl}/objednavka/{OrderId}</c>) so the consumer
/// (<c>EmailSendService</c>) passes it verbatim to SendGrid.
/// </para>
///
/// <para>
/// <b>No <c>TotalAmountMinor</c> / <c>Currency</c></b> fields — the customer
/// already received those on the T-0067 "thanks" email. The acceptance
/// email is shorter: "Your maker accepted — link to view status." Distinct
/// record keeps the substitution dictionary lean.
/// </para>
/// </summary>
public sealed record OrderAcceptedCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    string LanguageCode,
    string ActionUrl);
