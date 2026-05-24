namespace Makables.Core.Domain.Email;

/// <summary>
/// Discriminator for the email template catalog (T-0028). At launch the
/// platform sends three transactional emails — one per Phase-2 auth flow.
/// Catalog (orders, payouts, messages) lands in Phase-4+ as new values.
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
    /// "Reset your password" link. Outbox event:
    /// <see cref="Outbox.OutboxEventTypes.AuthPasswordResetSend"/>.
    /// </summary>
    AuthPasswordReset = 3,
}
