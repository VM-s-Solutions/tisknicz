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

    /// <summary>
    /// "Nová zpráva k objednávce {orderNumber}" customer-recipient digest.
    /// Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderMessagePostedCustomerEmail"/>.
    /// T-0079.
    /// </summary>
    OrderMessagePostedCustomer = 9,

    /// <summary>
    /// Symmetric maker-recipient digest. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderMessagePostedMakerEmail"/>.
    /// T-0079.
    /// </summary>
    OrderMessagePostedMaker = 10,

    /// <summary>
    /// "Vaše objednávka {orderNumber} byla zrušena (platba neproběhla)"
    /// customer notification on auto-expiry. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderCancelledCustomerEmail"/>.
    /// T-0083 ships AutoExpiry copy; Customer / Admin source extensions
    /// in T-0105 / T-0107.
    /// </summary>
    OrderCancelledAutoExpiryCustomer = 11,

    /// <summary>
    /// "Vrácení peněz k objednávce {orderNumber}" customer notification
    /// after an admin refund (full or partial — the payload's
    /// <c>IsFullRefund</c> flag picks the copy variant). Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderRefundedCustomerEmail"/>.
    /// T-0105.
    /// </summary>
    OrderRefundedCustomer = 12,

    /// <summary>
    /// "Nová reklamace k objednávce {orderNumber}" admin digest fired on
    /// every dispute open (customer / maker / carrier / admin source).
    /// Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderDisputedAdminEmail"/>.
    /// Recipient = <c>EmailOptions.AdminNotificationAddress</c>, resolved
    /// at send time. T-0106.
    /// </summary>
    OrderDisputedAdmin = 13,

    /// <summary>
    /// "Vaše reklamace byla vyřízena" customer notification with outcome
    /// + the admin's customer-visible notes. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.OrderDisputeResolvedCustomerEmail"/>.
    /// T-0106.
    /// </summary>
    OrderDisputeResolvedCustomer = 14,
}
