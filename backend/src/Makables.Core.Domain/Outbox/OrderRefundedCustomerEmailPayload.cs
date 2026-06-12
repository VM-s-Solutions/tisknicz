namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "Vrácení peněz k objednávce"
/// email, enqueued by <c>RefundOrder.Handler</c> after a successful
/// Comgate refund. T-0105 (US-admin-0008 AC-1).
///
/// <para>
/// <see cref="RefundedAmountMinor"/> is the amount of THIS refund event
/// (not the cumulative total) — the email tells the customer what just
/// landed back on their card. <see cref="IsFullRefund"/> is true when
/// the cumulative refunded total reached the order total and the order
/// transitioned to <c>Refunded</c>; the template picks the full-vs-partial
/// copy variant from it.
/// </para>
///
/// <para>
/// Enrichment-at-enqueue per the T-0067 pattern (pending Q-0012): the
/// full payload — including the pre-baked <see cref="ActionUrl"/> — is
/// serialized into the outbox row so the consumer never re-loads the
/// order. PascalCase JSON property names match the
/// <see cref="OrderCancelledCustomerEmailPayload"/> convention.
/// </para>
/// </summary>
public sealed record OrderRefundedCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    long RefundedAmountMinor,
    string Currency,
    bool IsFullRefund,
    string LanguageCode,
    string ActionUrl);
