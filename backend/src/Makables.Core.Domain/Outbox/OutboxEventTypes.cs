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
    /// True when <paramref name="eventType"/> routes to the
    /// <c>send-email</c> queue per T-0029 <c>OutboxDispatcher</c>. The
    /// routing table is one place — adding a fourth email event type
    /// is a one-line edit here, not two places.
    ///
    /// <para>
    /// <see cref="InvoiceGenerate"/> is intentionally NOT in this set —
    /// it routes to a separate queue (T-0069) so PDF render + upload
    /// failures do not contaminate the email-send retry budget.
    /// </para>
    /// </summary>
    public static bool IsEmailSend(string eventType) =>
        eventType is AuthMagicLinkSend
                  or AuthEmailConfirmationSend
                  or AuthPasswordResetSend
                  or OrderPaidCustomerEmail
                  or OrderPlacedMakerEmail;
}
