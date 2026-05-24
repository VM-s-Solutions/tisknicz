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
/// (Re-)send an email-confirmation link. Per ADR 0012 §Email confirmation.
///
/// Behaviour mirrors <see cref="RequestMagicLink"/>:
///   - Always returns Success (no enumeration leak).
///   - Unknown email / soft-deleted / already-confirmed → silent no-op.
///   - Rate limit: 3 requests per email per 10 minutes → silent no-op
///     when exhausted.
///   - The Register handler enqueues the FIRST confirmation token
///     directly on account creation; this handler is the user-driven
///     "resend" flow.
///
/// Per T-0023 security review B-1: the no-op branches pay the same
/// CountIssuedSince + CSPRNG + serialize cost as the happy path so
/// total LATENCY does not differ by enumeration. The sentinel user id
/// keeps the SQL plan identical when no user matches.
/// </summary>
public static class SendEmailConfirmation
{
    /// <summary>TTL per ADR 0012 §Email confirmation.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    public const string OutboxEventType = "auth.emailConfirmation.send";

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

            // Pay the rate-limit round-trip cost unconditionally (B-1).
            var rateLimitUserId = user?.Id ?? NoSuchUserSentinel;
            var since = now - RateLimitWindow;
            var recent = await tokens.CountIssuedSinceAsync(
                rateLimitUserId, OneTimeTokenPurpose.EmailConfirmation, since, cancellationToken);

            // Pay the mint + serialize cost unconditionally (B-1).
            var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var expiresAt = now + TokenLifetime;
            var payloadJson = JsonSerializer.Serialize(
                new OutboxPayload(user?.Id ?? string.Empty, user?.Email ?? string.Empty, raw, expiresAt));

            var willSend = user is not null
                        && user.IsActive
                        && user.EmailConfirmedAt is null
                        && recent < MaxRequestsPerWindow;

            if (!willSend)
            {
                logger.LogInformation("EmailConfirmation send for {EmailNormalized}: silent no-op.", emailNormalized);
                return BusinessResult.Success();
            }

            tokens.Add(OneTimeToken.Issue(
                tokenHash: hash,
                userId: user!.Id,
                purpose: OneTimeTokenPurpose.EmailConfirmation,
                expiresAt: expiresAt,
                now: now,
                ipAddress: command.IpAddress));

            outbox.Enqueue(user.Id, OutboxEventType, payloadJson);

            return BusinessResult.Success();
        }
    }
}
