namespace Makables.Core.Domain.Email;

/// <summary>
/// Discriminator for the email template catalog (T-0028). Phase 2 (auth
/// flows) shipped values 1–3; Phase 4 (orders) extends with 4–5 per
/// T-0067 (US-customer-0010 AC-4 + US-maker-0006). Payouts, messages and
/// the remaining catalog entries land with their respective tickets.
///
/// Stored as a <c>string</c> column (the enum name verbatim) so that
/// migrations and seed inserts are readable in SQL without joining to a
/// reference table.
/// </summary>
public enum EmailTemplateType
{
    /// <summary>
    /// One-time login link. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.AuthMagicLinkSend"/>.
    /// </summary>
    AuthMagicLink = 1,

    /// <summary>
    /// "Confirm your email address" sent on registration and on demand.
    /// Outbox event: <see cref="Outbox.OutboxEventTypes.AuthEmailConfirmationSend"/>.
    /// </summary>
    AuthEmailConfirmation = 2,

    /// <summary>
    /// "Reset your password" link. Outbox event: <see cref="Outbox.OutboxEventTypes.AuthPasswordResetSend"/>.
    /// </summary>
    AuthPasswordReset = 3,

    /// <summary>
    /// "Thanks for your order" customer confirmation. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderPaidCustomerEmail"/>. T-0067.
    /// </summary>
    OrderPaidCustomer = 4,

    /// <summary>
    /// "New order arrived" maker notification. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderPlacedMakerEmail"/>. T-0067.
    /// </summary>
    OrderPlacedMaker = 5,

    /// <summary>
    /// "Your maker accepted the order" customer notification. Outbox
    /// event: <see cref="Outbox.OutboxEventTypes.OrderAcceptedCustomerEmail"/>.
    /// T-0071.
    /// </summary>
    OrderAcceptedCustomer = 6,

    /// <summary>
    /// "Your order has shipped" customer notification — unified across
    /// the Zásilkovna (T-0072) + personal-pickup (T-0073) paths. Outbox
    /// event: <see cref="Outbox.OutboxEventTypes.OrderShippedCustomerEmail"/>.
    /// Template conditionally renders the tracking-URL row only when the
    /// payload's <c>TrackingUrl</c> field is non-empty.
    /// </summary>
    OrderShippedCustomer = 7,

    /// <summary>
    /// "Your order has been delivered" customer notification. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderDeliveredCustomerEmail"/>.
    /// Single email per delivery transition; no maker notification (T-0076
    /// locked decision A.2). T-0076.
    /// </summary>
    OrderDeliveredCustomer = 8,
}
