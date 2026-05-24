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
}
