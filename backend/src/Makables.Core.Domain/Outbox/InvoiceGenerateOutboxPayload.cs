namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the <c>invoice.generate</c> outbox event, enqueued
/// by <c>MarkOrderPaid.Handler</c> as its third outbox row after the
/// Comgate webhook transitions the order to <c>OrderState.Paid</c>.
/// T-0068b locked decision 7.
///
/// <para>
/// Consumer is the <c>GenerateInvoiceFunction</c> in T-0069 — it
/// deserialises this payload and dispatches <c>IssueInvoice.Command</c>
/// via Mediator. The handler is the only piece that touches the
/// renderer + blob store; the Function is a thin queue-trigger boundary.
/// </para>
///
/// <para>
/// <see cref="LanguageCode"/> is pre-resolved at enqueue time per the
/// T-0028 / T-0067 producer-side language-resolution pattern. T-0069's
/// invoice-email attachment step uses it to name the attachment
/// (<c>faktura-{orderNumber}.pdf</c> vs <c>invoice-{orderNumber}.pdf</c>)
/// — pre-resolving here means a customer who edits their profile
/// between order placement and invoice generation still receives the
/// PDF in whatever language they preferred at payment time.
/// </para>
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// (no <c>PropertyNamingPolicy</c>) round-trips every payload shape
/// in the outbox.
/// </para>
/// </summary>
public sealed record InvoiceGenerateOutboxPayload(
    string OrderId,
    string LanguageCode);
