namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the maker-facing "Výplata za dávku ... odeslána" email,
/// enqueued once per maker per batch by <c>MarkPayoutBatchCompleted.Handler</c>
/// at settlement (T-0103). Mirrors <see cref="PayoutFeeInvoiceMakerEmailPayload"/>'s
/// shape; PascalCase JSON property names match the order-email payload
/// convention.
///
/// <para>
/// <see cref="MakerTotalPaidMinor"/> is the sum of THIS maker's
/// <c>Order.MakerPayoutAmountMinor</c> across the batch — the per-maker slice,
/// never the cross-maker <c>PayoutBatch.TotalAmountMinor</c>.
/// <see cref="FeeInvoiceActionUrl"/> deep-links the maker fee-invoice page
/// (the T-0116 drill route). Unlike the fee-invoice email, NO PDF is attached
/// — this is a plain summary. Maker email + language are resolved at enqueue
/// time (enrichment-at-enqueue, pending Q-0012).
/// </para>
/// </summary>
public sealed record PayoutBatchPayoutSentMakerEmailPayload(
    string MakerId,
    string MakerEmail,
    string BatchNumber,
    long MakerTotalPaidMinor,
    string Currency,
    int OrderCount,
    string FeeInvoiceActionUrl,
    string LanguageCode);
