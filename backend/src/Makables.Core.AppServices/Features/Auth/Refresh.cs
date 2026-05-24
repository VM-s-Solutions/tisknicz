using System.Security.Cryptography;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Rotate a refresh token: validate it, mint a new access JWT + a new
/// refresh token in the same family, mark the old one revoked. Per ADR
/// 0012 §Refresh token.
///
/// Reuse detection: when the presented token is already revoked, every
/// active token in its family is revoked too (catches stolen-token
/// replay). The caller still receives <see cref="BusinessErrorMessage.AuthRequired"/>
/// so the attacker can't tell whether the token was valid-once or
/// always-bad.
/// </summary>
public static class Refresh
{
    public sealed record Command(
        string RawRefreshToken,
        string Audience,
        string? UserAgent,
        string? IpAddress) : ICommand<SessionResult>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RawRefreshToken)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.Audience)
                .Must(MakablesAudiences.IsValid)
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        IJwtIssuer jwt,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<SessionResult>>
    {
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

        public async Task<BusinessResult<SessionResult>> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var presentedHash = RefreshTokenHasher.Sha256Hex(command.RawRefreshToken);

            var token = await refreshTokens.GetByTokenHashAsync(presentedHash, cancellationToken);
            if (token is null)
            {
                return Unauthorized();
            }

            // Reuse detection: a token that was already revoked means the
            // attacker has it after a legitimate rotation. Burn the whole
            // family per ADR 0012 §Refresh token.
            if (token.RevokedAt is not null)
            {
                logger.LogWarning(
                    "Refresh-token reuse detected for family {FamilyId}; revoking all active members.",
                    token.FamilyId);
                var family = await refreshTokens.GetActiveByFamilyAsync(token.FamilyId, cancellationToken);
                foreach (var sibling in family)
                {
                    sibling.Revoke(now);
                }
                return Unauthorized();
            }

            if (!token.IsActiveAt(now))
            {
                return Unauthorized();
            }

            var user = await users.GetByIdAsync(token.UserId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                token.Revoke(now);
                return Unauthorized();
            }

            // Audience must remain stable across a refresh — a token issued
            // for `customer` cannot be exchanged for a `maker` token.
            // Admins can refresh into any audience (parallels Login).
            if (user.Role != UserRole.Admin && command.Audience != user.Role.ToString().ToLowerInvariant())
            {
                return BusinessResult.Failure<SessionResult>(Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            // Issue rotation. The old token gets marked rotated -> new one
            // is added in the same family.
            var (rawNew, newHash) = GenerateRefreshToken();
            var newId = ids.Next();
            var newExpiresAt = now + RefreshTokenLifetime;
            var rotated = token.IssueRotation(newId, newHash, newExpiresAt, command.UserAgent, command.IpAddress);
            token.MarkRotated(newId, now);
            refreshTokens.Add(rotated);

            var access = jwt.Issue(user, command.Audience, now);
            return BusinessResult.Success(new SessionResult(
                UserId: user.Id,
                AccessToken: access.Token,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: rawNew,
                RefreshTokenExpiresAt: newExpiresAt));
        }

        private static BusinessResult<SessionResult> Unauthorized() =>
            BusinessResult.Failure<SessionResult>(Error.Unauthorized(BusinessErrorMessage.AuthRequired));

        private static (string Raw, string Hash) GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var raw = Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = RefreshTokenHasher.Sha256Hex(raw);
            return (raw, hash);
        }
    }
}
