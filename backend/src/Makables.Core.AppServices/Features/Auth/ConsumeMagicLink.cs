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
/// Behaviour:
///   - Token missing / wrong purpose / expired / already consumed →
///     <see cref="BusinessErrorMessage.AuthMagicLinkInvalid"/>. Single
///     generic code so an attacker can't tell which condition fired.
///   - Token valid + user soft-deleted → same invalid code; the token is
///     also marked consumed so a stolen valid link can't be replayed
///     after the account comes back.
///   - Token valid + audience mismatch for non-admin → forbidden.
///   - Happy path: mark consumed, confirm the email if it wasn't already
///     (a successful magic-link redemption proves the user controls the
///     inbox, which is what email confirmation is for), reset lockout
///     counters, issue access + refresh tokens.
///
/// <see cref="IPersistOnFailureCommand"/>: every non-happy path that
/// touches state (marking the token consumed on soft-deleted-user or
/// audience-mismatch) MUST persist so the token can't be replayed.
/// </summary>
public static class ConsumeMagicLink
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

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
                // Burn the token so a stolen link can't be re-presented if
                // the user is reactivated later.
                token.Consume(now);
                return Invalid();
            }

            if (!user.MatchesAudience(command.Audience))
            {
                token.Consume(now);
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            // Happy path. Burn the token, mark the email confirmed (the
            // user just proved inbox control), and mint a session.
            token.Consume(now);
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
