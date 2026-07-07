namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Canonical outbox-event-type strings. Both the producers (use-case
/// handlers in <c>Core.AppServices.Features.*</c>) and the consumer
/// (<c>ProcessOutboxFunction</c> in <c>Makables.Functions</c>, T-0029)
/// reference these. Per ADR 0020.
///
/// Convention: <c>&lt;domain&gt;.&lt;action&gt;.&lt;modality&gt;</c>
/// — e.g. <c>auth.magicLink.send</c> means "the auth domain wants the
/// outbox processor to send a magic-link email."
/// </summary>
public static class OutboxEventTypes
{
    /// <summary>Magic-link sign-in email (T-0023 §RequestMagicLink).</summary>
    public const string AuthMagicLinkSend = "auth.magicLink.send";

    /// <summary>Email-confirmation email (T-0024 §Register + SendEmailConfirmation).</summary>
    public const string AuthEmailConfirmationSend = "auth.emailConfirmation.send";

    /// <summary>Password-reset email (T-0025 §RequestPasswordReset).</summary>
    public const string AuthPasswordResetSend = "auth.passwordReset.send";

    /// <summary>
    /// "Thanks for your order" confirmation email to the customer, fired
    /// by <see cref="Features.Orders.MarkOrderPaid"/> after the Comgate
    /// webhook transitions the order to <see cref="Orders.OrderState.Paid"/>.
    /// T-0067 (US-customer-0010 AC-4).
    /// </summary>
    public const string OrderPaidCustomerEmail = "order.paid.customerEmail";

    /// <summary>
    /// "New order arrived" notification email to the maker, fired by
    /// <see cref="Features.Orders.MarkOrderPaid"/> alongside the customer
    /// email. T-0067 (US-maker-0006).
    /// </summary>
    public const string OrderPlacedMakerEmail = "order.placed.makerEmail";

    /// <summary>
    /// "Generate the invoice PDF" event, fired by
    /// <see cref="Features.Orders.MarkOrderPaid"/> as its third outbox
    /// enqueue per T-0068b. Consumer is the <c>GenerateInvoiceFunction</c>
    /// landing in T-0069 — it dispatches <c>IssueInvoice.Command</c> via
    /// Mediator. Distinct routing from the email queue because this event
    /// drives PDF rendering + blob upload, not an email send.
    /// </summary>
    public const string InvoiceGenerate = "invoice.generate";

    /// <summary>
    /// "Your maker accepted the order" customer notification, fired by
    /// <c>AcceptOrder.Handler</c> after the Paid → Accepted state
    /// transition. T-0071 (US-maker-0006).
    /// </summary>
    public const string OrderAcceptedCustomerEmail = "order.accepted.customerEmail";

    /// <summary>
    /// "Your order has shipped" customer notification, fired by both the
    /// Zásilkovna <c>ShipOrder.Handler</c> (T-0072) and the personal-pickup
    /// <c>HandOverOrder.Handler</c> (T-0073). Single unified event; the
    /// payload's nullable <c>TrackingUrl</c> field discriminates between
    /// the two paths (template conditionally renders the tracking row).
    /// </summary>
    public const string OrderShippedCustomerEmail = "order.shipped.customerEmail";

    /// <summary>
    /// "Generate the shipping label PDF" event, fired by Zásilkovna
    /// <c>ShipOrder.Handler</c> (T-0072) atomically with the customer
    /// shipped-email event. Consumer is <c>GenerateLabelFunction</c>
    /// (T-0074) — it dispatches <c>FetchAndStoreShippingLabel.Command</c>
    /// via Mediator. Routes to its own <c>generate-label</c> queue per
    /// ADR 0020 queue-per-event-class split so a slow Packeta label PDF
    /// download doesn't contaminate the email or invoice retry budgets.
    /// Personal-pickup (T-0073) does NOT emit this event — no label.
    /// </summary>
    public const string ShippingGenerateLabel = "shipping.generate.label";

    /// <summary>
    /// "Your order has been delivered" customer notification email, fired
    /// by <see cref="Features.Orders.MarkOrderDelivered"/> after the
    /// Shipped → Delivered transition. Single email per delivery (no
    /// maker email per T-0076 locked decision A.2). Routes through the
    /// existing <c>send-email</c> queue. T-0076 (US-customer-0013).
    /// </summary>
    public const string OrderDeliveredCustomerEmail = "order.delivered.customerEmail";

    /// <summary>
    /// "A dispute was opened on order #..." admin notification, fired by
    /// every open-dispute path (customer / maker / carrier-sourced via
    /// the rewired <c>DisputeShipment</c> / admin). Recipient resolves at
    /// SEND time from <c>EmailOptions.AdminNotificationAddress</c> —
    /// deliberately NOT baked into the payload so a missing config parks
    /// the outbox row instead of blocking the dispute open (T-0106 §C.9).
    /// Replaces the retired T-0078 <c>order.disputed.carrierSourced</c>
    /// stub event (never routed).
    /// </summary>
    public const string OrderDisputedAdminEmail = "order.disputed.adminEmail";

    /// <summary>
    /// "Your dispute was resolved" customer notification with the outcome
    /// + the admin's customer-visible resolution notes, fired by
    /// <c>ResolveDispute.Handler</c>. T-0106 (US-admin-0011 AC-2; the
    /// maker counterpart is deliberately out of scope at MVP).
    /// </summary>
    public const string OrderDisputeResolvedCustomerEmail = "order.disputeResolved.customerEmail";

    /// <summary>
    /// "Customer has a new message on order #..." digest email, enqueued
    /// by <see cref="Features.OrderMessages.PostMakerOrderMessage"/> when
    /// a maker posts to the thread AND the customer's recipient
    /// debounce window has elapsed (or is null). T-0079 (US-customer-0014
    /// AC-3 / US-maker-0011 AC-1).
    /// </summary>
    public const string OrderMessagePostedCustomerEmail = "order.message.posted.customerEmail";

    /// <summary>
    /// "Maker has a new message on order #..." symmetric digest email,
    /// enqueued by <see cref="Features.OrderMessages.PostCustomerOrderMessage"/>
    /// when a customer posts AND the maker's recipient debounce window
    /// has elapsed. T-0079 (US-maker-0011 AC-2).
    /// </summary>
    public const string OrderMessagePostedMakerEmail = "order.message.posted.makerEmail";

    /// <summary>
    /// "Your order expired without payment" customer notification, fired
    /// by <see cref="Features.Orders.CancelExpiredOrder"/> after the
    /// auto-cancel transition. T-0083 (US-customer-0010 AC-3). No maker
    /// counterpart per T-0083 locked decision A.2 — the maker never knew
    /// about the order at PendingPayment state.
    /// </summary>
    public const string OrderCancelledCustomerEmail = "order.cancelled.customerEmail";

    /// <summary>
    /// "Your order has been refunded" customer notification, fired by
    /// <see cref="Features.Orders.RefundOrder"/> after a successful
    /// Comgate refund — full AND partial (the payload's
    /// <c>IsFullRefund</c> flag picks the copy variant). T-0105
    /// (US-admin-0008 AC-1).
    /// </summary>
    public const string OrderRefundedCustomerEmail = "order.refunded.customerEmail";

    /// <summary>
    /// "Your platform-fee invoice for this payout batch" maker notification
    /// with the Fee invoice PDF attached, enqueued per maker by the T-0102b
    /// <c>PayoutArtifactService</c> at batch creation. The PDF is looked up
    /// at SEND time (T-0069 attachment pattern), never baked into the
    /// payload. Routes through the existing <c>send-email</c> queue.
    /// </summary>
    public const string PayoutFeeInvoiceMakerEmail = "payout.feeInvoice.makerEmail";

    /// <summary>
    /// "Your payout for batch {{batch_number}} has been sent" maker
    /// notification — one per maker per batch, enqueued by
    /// <c>MarkPayoutBatchCompleted.Handler</c> at settlement. Summarizes the
    /// total paid to that maker + the order count + a deep link to the maker
    /// fee-invoice page. No PDF attachment. Routes through the existing
    /// <c>send-email</c> queue. T-0103.
    /// </summary>
    public const string PayoutBatchPayoutSentMakerEmail = "payout.payoutSent.makerEmail";

    /// <summary>
    /// "Maker missed the 7-day response window" admin notification, fired
    /// by the daily <c>DisputeAutoEscalationFunction</c> sweep's
    /// <c>EscalateDispute</c> handler for a customer-sourced dispute past
    /// <c>Dispute.CreatedAt + 7 days</c> with no maker reply. Recipient
    /// resolves at SEND time from <c>EmailOptions.AdminNotificationAddress</c>
    /// — same as <see cref="OrderDisputedAdminEmail"/>. Notification only —
    /// never resolves the dispute or sanctions the maker. T-0145.
    /// </summary>
    public const string DisputeAutoEscalatedAdminEmail = "dispute.autoEscalated.adminEmail";

    /// <summary>
    /// True when <paramref name="eventType"/> routes to the
    /// <c>send-email</c> queue per T-0029 <c>OutboxDispatcher</c>. The
    /// routing table is one place — adding a new email event type
    /// is a one-line edit here, not two places.
    ///
    /// <para>
    /// <see cref="InvoiceGenerate"/> and <see cref="ShippingGenerateLabel"/>
    /// are intentionally NOT in this set — they route to separate queues
    /// (T-0069 / T-0074) so PDF render + upload failures do not
    /// contaminate the email-send retry budget.
    /// </para>
    /// </summary>
    public static bool IsEmailSend(string eventType) =>
        eventType is AuthMagicLinkSend
                  or AuthEmailConfirmationSend
                  or AuthPasswordResetSend
                  or OrderPaidCustomerEmail
                  or OrderPlacedMakerEmail
                  or OrderAcceptedCustomerEmail
                  or OrderShippedCustomerEmail
                  or OrderDeliveredCustomerEmail
                  or OrderMessagePostedCustomerEmail
                  or OrderMessagePostedMakerEmail
                  or OrderCancelledCustomerEmail
                  or OrderRefundedCustomerEmail
                  or OrderDisputedAdminEmail
                  or OrderDisputeResolvedCustomerEmail
                  or PayoutFeeInvoiceMakerEmail
                  or PayoutBatchPayoutSentMakerEmail
                  or DisputeAutoEscalatedAdminEmail;

    /// <summary>
    /// True when <paramref name="eventType"/> routes to the
    /// <c>generate-invoice</c> queue per T-0069 <c>OutboxDispatcher</c>
    /// routing branch. Disjoint from <see cref="IsEmailSend"/> +
    /// <see cref="IsGenerateLabel"/> — a single event type cannot route
    /// to multiple queues. The dispatcher classifies once per event,
    /// then publishes to the matching per-event-type queue.
    /// </summary>
    public static bool IsInvoiceGenerate(string eventType) =>
        eventType == InvoiceGenerate;

    /// <summary>
    /// True when <paramref name="eventType"/> routes to the
    /// <c>generate-label</c> queue per T-0074 <c>OutboxDispatcher</c>
    /// routing branch. Disjoint from <see cref="IsEmailSend"/> +
    /// <see cref="IsInvoiceGenerate"/>. Per ADR 0020 queue-per-event-class
    /// split — a slow Packeta label download cannot stall email sends.
    /// </summary>
    public static bool IsGenerateLabel(string eventType) =>
        eventType == ShippingGenerateLabel;
}
