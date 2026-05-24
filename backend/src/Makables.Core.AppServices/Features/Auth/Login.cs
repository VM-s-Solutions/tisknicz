using System.Security.Cryptography;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Email + password login. Per ADR 0012 §1, §JWT, §Refresh token, §Lockout.
///
/// Failure outcomes (all return the SAME error code <see cref="BusinessErrorMessage.AuthInvalidCredentials"/>
/// on the wire so the response doesn't leak whether the email exists):
///   - Email unknown → register a ghost-bucket attempt, then fail.
///   - Email known + password wrong → register attempt against User,
///     mirror to the bucket for cross-restart safety.
///   - Either side is locked → fail with <see cref="BusinessErrorMessage.AuthLocked"/>.
///   - Email known + password correct BUT email not confirmed → fail with
///     <see cref="BusinessErrorMessage.AuthEmailNotConfirmed"/>.
///   - Email known + soft-deleted → AuthInvalidCredentials (same leak guard).
///
/// On success: reset both counters, issue access + refresh tokens, persist
/// the refresh-token row keyed by its SHA-256 hash.
/// </summary>
public static class Login
{
    public sealed record Command(
        string Email,
        string Password,
        string Audience,
        string? UserAgent,
        string? IpAddress) : ICommand<SessionResult>;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.Password)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(200).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.Audience)
                .Must(MakablesAudiences.IsValid)
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        ILoginAttemptBucketRepository buckets,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IJwtIssuer jwt,
        IIdGenerator ids,
        IClock clock,
        IOptions<LockoutOptions> lockoutOptions,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<SessionResult>>
    {
        // Per ADR 0012 §Refresh token: 30 days, single-use.
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

        public async Task<BusinessResult<SessionResult>> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var emailNormalized = User.NormalizeEmail(command.Email);
            var lockout = lockoutOptions.Value;

            // 1. Resolve (or create) the per-email bucket FIRST so unknown
            //    emails consume ghost slots identically to known ones.
            var bucket = await buckets.GetAsync(emailNormalized, cancellationToken);
            if (bucket is null)
            {
                bucket = LoginAttemptBucket.Create(emailNormalized, now);
                buckets.Add(bucket);
            }

            if (bucket.IsLocked(now))
            {
                logger.LogInformation("Login rejected: bucket locked for {EmailNormalized} until {LockedUntil:o}",
                    emailNormalized, bucket.LockedUntil);
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthLocked));
            }

            // 2. Look up the user. Returns soft-deleted rows too (B-1 fix)
            //    so we can mint the same generic credentials error.
            var user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);
            if (user is null || !user.IsActive || user.PasswordHash is null)
            {
                // Burn a bucket slot for the unknown / soft-deleted case
                // so an attacker can't enumerate emails by latency.
                bucket.RegisterFailedAttempt(now, lockout.Threshold, lockout.Window);
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthInvalidCredentials));
            }

            if (user.IsLocked(now))
            {
                // User row's lockout takes priority over the bucket if both
                // are set (the user row may have been locked by an admin tool).
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthLocked));
            }

            if (!hasher.Verify(command.Password, user.PasswordHash))
            {
                user.RegisterFailedLogin(now, lockout.Threshold, lockout.Window);
                bucket.RegisterFailedAttempt(now, lockout.Threshold, lockout.Window);
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthInvalidCredentials));
            }

            // Password OK. Now check the post-auth gates.
            if (user.EmailConfirmedAt is null)
            {
                // Per ADR 0012 §Email confirmation: order placement is
                // gated on a confirmed email; surfacing the specific
                // error is intentional so the UI can prompt for re-send.
                return BusinessResult.Failure<SessionResult>(
                    Error.Validation("email", BusinessErrorMessage.AuthEmailNotConfirmed));
            }

            // Admin login is permitted from any host; non-admin tokens
            // bind to the requested audience. T-0027's middleware enforces
            // audience binding on every protected endpoint.
            var audience = command.Audience;
            if (user.Role != UserRole.Admin && audience != user.Role.ToString().ToLowerInvariant())
            {
                return BusinessResult.Failure<SessionResult>(
                    Error.Forbidden(BusinessErrorMessage.AuthForbidden));
            }

            // 3. Success path. Reset counters, transparently re-hash if
            //    the stored hash is older than current policy, mint tokens.
            user.RegisterSuccessfulLogin();
            bucket.Reset(now);

            if (hasher.NeedsRehash(user.PasswordHash))
            {
                user.SetPasswordHash(hasher.Hash(command.Password));
            }

            var access = jwt.Issue(user, audience, now);
            var (rawRefresh, refreshHash) = GenerateRefreshToken();
            var refreshId = ids.Next();
            var familyId = ids.Next();
            var refreshExpiresAt = now + RefreshTokenLifetime;

            refreshTokens.Add(RefreshToken.IssueNew(
                id: refreshId,
                userId: user.Id,
                tokenHash: refreshHash,
                familyId: familyId,
                expiresAt: refreshExpiresAt,
                countryCode: user.CountryCodePrimary,
                userAgent: command.UserAgent,
                ipAddress: command.IpAddress));

            return BusinessResult.Success(new SessionResult(
                UserId: user.Id,
                AccessToken: access.Token,
                AccessTokenExpiresAt: access.ExpiresAt,
                RefreshToken: rawRefresh,
                RefreshTokenExpiresAt: refreshExpiresAt));
        }

        private static (string Raw, string Hash) GenerateRefreshToken()
        {
            // 32 bytes of CSPRNG = 256 bits of entropy. URL-safe base64 so
            // the client can carry it in a cookie value without escaping.
            var bytes = RandomNumberGenerator.GetBytes(32);
            var raw = Convert.ToBase64String(bytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = RefreshTokenHasher.Sha256Hex(raw);
            return (raw, hash);
        }
    }
}
