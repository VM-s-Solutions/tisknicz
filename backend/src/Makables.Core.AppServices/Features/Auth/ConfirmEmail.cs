using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Confirm an email by exchanging the token issued at registration (or
/// by <see cref="SendEmailConfirmation"/>'s resend flow). Per ADR 0012
/// §Email confirmation.
///
/// Unlike <see cref="ConsumeMagicLink"/> this does NOT mint a session —
/// the user logs in normally afterward.
///
/// Single generic error code <see cref="BusinessErrorMessage.AuthEmailConfirmationInvalid"/>
/// for every reject reason (missing, wrong purpose, expired, consumed,
/// soft-deleted user, lost race) so the caller can't enumerate.
///
/// Atomic claim via <see cref="IOneTimeTokenRepository.TryConsumeAsync"/>
/// prevents double-redemption races (per T-0023 review M-1 — same class
/// of bug applies here).
/// </summary>
public static class ConfirmEmail
{
    /// <summary>
    /// Outcome-idempotency window (T-0168, audit AUTH-M1): mail-scanner
    /// prefetch, a page refresh or a second click burns the one-time
    /// token, then told an already-CONFIRMED user their link is invalid
    /// with no way forward. A replay of a token consumed within this
    /// window whose user is confirmed reports success instead.
    /// </summary>
    public static readonly TimeSpan AlreadyConfirmedGrace = TimeSpan.FromHours(24);

    public sealed record Command(string RawToken) : ICommand, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RawToken)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOneTimeTokenRepository tokens,
        IUserRepository users,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var hash = OpaqueTokenFactory.Sha256Hex(command.RawToken);

            // Pre-read for purpose check — claiming a MagicLink or
            // PasswordReset token from this handler would silently burn it.
            var token = await tokens.GetByHashAsync(hash, cancellationToken);
            if (token is null || token.Purpose != OneTimeTokenPurpose.EmailConfirmation)
            {
                return Invalid();
            }
            if (!token.IsRedeemable(now))
            {
                // Replay of a recently consumed token: the caller holds the
                // REAL token (nothing enumerable), so if its user is already
                // confirmed the truthful answer is success, not "invalid
                // link". Expired-but-never-consumed tokens fall through to
                // Invalid (ConsumedAt is null).
                if (token.ConsumedAt is { } consumedAt
                    && now - consumedAt <= AlreadyConfirmedGrace)
                {
                    var replayUser = await users.GetByIdAsync(token.UserId, cancellationToken);
                    if (replayUser is { IsActive: true, EmailConfirmedAt: not null })
                    {
                        logger.LogInformation(
                            "Email-confirmation replay within grace for {UserId}.", replayUser.Id);
                        return BusinessResult.Success();
                    }
                }
                return Invalid();
            }

            // Atomic claim. Loser of a concurrent-redemption race exits
            // with Invalid() — same shape as the wrong-token path so the
            // outcomes are indistinguishable.
            var claimed = await tokens.TryConsumeAsync(hash, now, cancellationToken);
            if (!claimed)
            {
                return Invalid();
            }

            var user = await users.GetByIdAsync(token.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                // Token already burned by the claim above; soft-deleted
                // users cannot have their email confirmed (they have to
                // be reactivated first). Return Invalid so the response
                // shape doesn't leak account state.
                return Invalid();
            }

            user.ConfirmEmail(now);

            logger.LogInformation("Email confirmed for {UserId}.", user.Id);
            return BusinessResult.Success();
        }

        private static BusinessResult Invalid() =>
            BusinessResult.Failure(Error.Validation("token", BusinessErrorMessage.AuthEmailConfirmationInvalid));
    }
}
