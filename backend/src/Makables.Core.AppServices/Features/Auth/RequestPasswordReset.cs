using System.Text.Json;
using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Issue a password-reset email. Per ADR 0012 §Password reset.
///
/// Behaviour:
///   - Always returns Success (no enumeration).
///   - Unknown / soft-deleted → silent no-op.
///   - Rate-limited (3 per 10 min) → silent no-op.
///   - Happy path: invalidate any still-redeemable prior reset tokens
///     for this user (so a previously-emailed link can't compete with a
///     newer one), mint a fresh 1-hour token, enqueue the outbox event.
///
/// Per T-0023 security baseline: ALWAYS pays the same CountIssuedSince +
/// CSPRNG + serialize cost via the sentinel user id so total LATENCY
/// does not differ by enumeration.
/// </summary>
public static class RequestPasswordReset
{
    /// <summary>TTL per ADR 0012 §Password reset.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    public const string OutboxEventType = "auth.passwordReset.send";

    private const string NoSuchUserSentinel = "__no-such-user__";

    public sealed record Command(string Email, string? IpAddress) : ICommand, IPersistOnFailureCommand;

    public sealed record OutboxPayload(string UserId, string Email, string RawToken, DateTimeOffset ExpiresAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Email)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(320).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IUserRepository users,
        IOneTimeTokenRepository tokens,
        IOutbox outbox,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var emailNormalized = User.NormalizeEmail(command.Email);

            var user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);

            // Pay the rate-limit cost unconditionally (B-1 timing).
            var rateLimitUserId = user?.Id ?? NoSuchUserSentinel;
            var since = now - RateLimitWindow;
            var recent = await tokens.CountIssuedSinceAsync(
                rateLimitUserId, OneTimeTokenPurpose.PasswordReset, since, cancellationToken);

            // Always mint + serialize (B-1).
            var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var expiresAt = now + TokenLifetime;
            var payloadJson = JsonSerializer.Serialize(
                new OutboxPayload(user?.Id ?? string.Empty, user?.Email ?? string.Empty, raw, expiresAt));

            var willSend = user is not null
                        && user.IsActive
                        && recent < MaxRequestsPerWindow;

            if (!willSend)
            {
                logger.LogInformation("PasswordReset request for {EmailNormalized}: silent no-op.", emailNormalized);
                return BusinessResult.Success();
            }

            // Invalidate any prior still-redeemable reset tokens so the
            // previously-emailed link can't compete with a newer one.
            // Per ADR 0012 §Password reset.
            await tokens.InvalidateRedeemableAsync(
                user!.Id, OneTimeTokenPurpose.PasswordReset, now, cancellationToken);

            tokens.Add(OneTimeToken.Issue(
                tokenHash: hash,
                userId: user.Id,
                purpose: OneTimeTokenPurpose.PasswordReset,
                expiresAt: expiresAt,
                now: now,
                ipAddress: command.IpAddress));

            outbox.Enqueue(user.Id, OutboxEventType, payloadJson);

            return BusinessResult.Success();
        }
    }
}
