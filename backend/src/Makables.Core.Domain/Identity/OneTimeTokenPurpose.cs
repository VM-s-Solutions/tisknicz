namespace Makables.Core.Domain.Identity;

/// <summary>
/// What a <see cref="OneTimeToken"/> can be exchanged for. Each purpose
/// has its own use case in <c>Core.AppServices.Features.Auth</c>; the
/// purpose discriminator on the entity prevents a token issued for one
/// flow from being consumed by another (e.g. an email-confirmation token
/// MUST NOT mint a session).
/// </summary>
public enum OneTimeTokenPurpose
{
    /// <summary>Passwordless sign-in (ADR 0012 §Magic link).</summary>
    MagicLink = 0,

    /// <summary>One-time link to confirm an email after registration (ADR 0012 §Email confirmation).</summary>
    EmailConfirmation = 1,

    /// <summary>Reset a forgotten password (ADR 0012 §Password reset).</summary>
    PasswordReset = 2,
}
