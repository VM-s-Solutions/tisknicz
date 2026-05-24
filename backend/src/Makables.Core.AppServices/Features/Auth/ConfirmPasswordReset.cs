using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Set a new password by exchanging a reset token. Per ADR 0012
/// §Password reset.
///
/// Side effects on the happy path:
///   - Burn the reset token (atomic claim, no double-redemption).
///   - Hash + store the new password; this also resets the lockout
///     counter on the User entity (see <see cref="User.SetPasswordHash"/>).
///   - Revoke ALL active refresh tokens for the user so every other
///     session is forcibly logged out — ADR 0012 §Password reset
///     ("force re-login everywhere").
///
/// Failure outcomes collapse to <see cref="BusinessErrorMessage.AuthPasswordResetInvalid"/>
/// — the caller cannot distinguish missing / wrong-purpose / expired /
/// consumed / lost-race / soft-deleted-user.
///
/// Does NOT mint a session; the user logs in normally with the new
/// password.
/// </summary>
public static class ConfirmPasswordReset
{
    public sealed record Command(string RawToken, string NewPassword) : ICommand, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RawToken)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);

            // Same password policy as Register (ADR 0012 §Password policy:
            // min 10 chars, no other complexity).
            RuleFor(c => c.NewPassword)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MinimumLength(10).WithErrorCode(BusinessErrorMessage.MinLength)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOneTimeTokenRepository tokens,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var hash = OpaqueTokenFactory.Sha256Hex(command.RawToken);

            // Pre-read for purpose check — claiming a MagicLink or
            // EmailConfirmation token from this handler would silently
            // burn it.
            var token = await tokens.GetByHashAsync(hash, cancellationToken);
            if (token is null
                || token.Purpose != OneTimeTokenPurpose.PasswordReset
                || !token.IsRedeemable(now))
            {
                return Invalid();
            }

            // Atomic claim. Loser of a concurrent-redemption race exits
            // with Invalid() — prevents two browser tabs from both
            // "succeeding" and racing on which password sticks.
            var claimed = await tokens.TryConsumeAsync(hash, now, cancellationToken);
            if (!claimed)
            {
                return Invalid();
            }

            var user = await users.GetByIdAsync(token.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                // Token already burned by the claim above; soft-deleted
                // users cannot reset (admin would need to reactivate).
                return Invalid();
            }

            user.SetPasswordHash(hasher.Hash(command.NewPassword));

            // Force re-login everywhere per ADR 0012 §Password reset.
            // A password reset implies the previous credentials were
            // potentially compromised; every minted access JWT will
            // expire on its own (15 min) but refresh tokens last 30 days,
            // so we must revoke them explicitly.
            var activeRefreshTokens = await refreshTokens.GetActiveByUserAsync(user.Id, cancellationToken);
            foreach (var rt in activeRefreshTokens)
            {
                rt.Revoke(now);
            }

            logger.LogInformation(
                "Password reset for {UserId}: hash rotated, {RevokedCount} refresh tokens revoked.",
                user.Id, activeRefreshTokens.Count);

            return BusinessResult.Success();
        }

        private static BusinessResult Invalid() =>
            BusinessResult.Failure(Error.Validation("token", BusinessErrorMessage.AuthPasswordResetInvalid));
    }
}
