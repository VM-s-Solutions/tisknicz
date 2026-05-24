using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Exchange a magic-link token for a session. Per ADR 0012 §Magic link.
///
/// Failure outcomes (collapsed to <see cref="BusinessErrorMessage.AuthMagicLinkInvalid"/>
/// to deny probe channels):
///   - Token missing / wrong purpose / expired / already consumed.
///   - Token claim race lost (another concurrent request consumed it).
///   - Audience mismatch for a non-admin user. The token is NOT burned
///     in this case — burning on cross-audience presentation would let
///     anyone with the URL deny the magic link to the legitimate user
///     (T-0023 security review M-2). The legitimate user can still
///     redeem from the correct audience.
///   - Token valid + user soft-deleted → the token IS burned (a stolen
///     valid link must not be replayable after reactivation).
///
/// Happy path: confirm the email if not already, reset lockout counters
/// (a successful passwordless sign-in is morally a successful auth
/// event), mint access + refresh tokens.
/// </summary>
public static class ConsumeMagicLink
{
    private static readonly TimeSpan RefreshTokenLifetime = RefreshToken.DefaultLifetime;

    public sealed record Command(
        string RawToken,
        string Audience,
        string? UserAgent,
        string? IpAddress) : ICommand<SessionResult>, IPersistOnFailureCommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RawToken)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.Audience)
                .Must(MakablesAudiences.IsValid)
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(
        IOneTimeTokenRepository tokens,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IJwtIssuer jwt,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<SessionResult>>
    {
        public async Task<BusinessResult<SessionResult>> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var hash = OpaqueTokenFactory.Sha256Hex(command.RawToken);

            // Pre-read for purpose + audience checks BEFORE the atomic claim.
            // We must know the purpose so we don't burn an EmailConfirmation
            // or PasswordReset token from this handler. We must know the
            // audience so a wrong-audience attempt doesn't burn the token
            // (M-2 fix).
            var token = await tokens.GetByHashAsync(hash, cancellationToken);
            if (token is null
                || token.Purpose != OneTimeTokenPurpose.MagicLink
                || !token.IsRedeemable(now))
            {
                return Invalid();
            }

            var user = await users.GetByIdAsync(token.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                // Soft-deleted / deleted users: burn the token so a stolen
                // valid link can't be replayed if the account comes back.
                // Best-effort claim; if the race-claim fails we still
                // return Invalid().
                _ = await tokens.TryConsumeAsync(hash, now, cancellationToken);
                return Invalid();
            }

            if (!user.MatchesAudience(command.Audience))
            {
                // Audience mismatch: DO NOT burn. The token remains
                // redeemable from the correct audience host. Per T-0023
                // security review M-2.
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            // Atomic claim. Exactly one of two concurrent requests wins;
            // the loser observes affected-rows = 0 and returns Invalid.
            // Per T-0023 security review M-1.
            var claimed = await tokens.TryConsumeAsync(hash, now, cancellationToken);
            if (!claimed)
            {
                return Invalid();
            }

            // Happy path. Reset lockout counters (a successful passwordless
            // sign-in is a successful auth event and should clear failed
            // password attempts on the same account — otherwise a magic-link
            // user with a stale failed-password streak could still be
            // locked after redemption), confirm the email if it wasn't
            // already (redemption proves inbox control), mint the session.
            user.ConfirmEmail(now);
            user.RegisterSuccessfulLogin();

            var access = jwt.Issue(user, command.Audience, now);
            var (rawRefresh, refreshHash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var refreshExpiresAt = now + RefreshTokenLifetime;

            refreshTokens.Add(RefreshToken.IssueNew(
                id: ids.Next(),
                userId: user.Id,
                tokenHash: refreshHash,
                familyId: ids.Next(),
                expiresAt: refreshExpiresAt,
                countryCode: user.CountryCodePrimary,
                userAgent: command.UserAgent,
                ipAddress: command.IpAddress));

            logger.LogInformation("Magic-link redeemed for {UserId} (audience {Audience}).", user.Id, command.Audience);

            return BusinessResult.Success(new SessionResult(
                UserId: user.Id,
                AccessToken: access.Token,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: rawRefresh,
                RefreshTokenExpiresAt: refreshExpiresAt));
        }

        private static BusinessResult<SessionResult> Invalid() =>
            BusinessResult.Failure<SessionResult>(
                Error.Validation("token", BusinessErrorMessage.AuthMagicLinkInvalid));
    }
}
