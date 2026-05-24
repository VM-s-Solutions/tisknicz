using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
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
            if (token is null
                || token.Purpose != OneTimeTokenPurpose.EmailConfirmation
                || !token.IsRedeemable(now))
            {
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
