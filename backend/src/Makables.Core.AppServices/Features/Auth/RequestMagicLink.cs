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
/// Issue a magic-link email for an existing account. Per ADR 0012
/// §Magic link.
///
/// Behaviour:
///   - Email unknown / soft-deleted → return Success (no leak). NO email
///     enqueued. NO token persisted.
///   - Email known, request budget exhausted ("3 per 10 minutes") →
///     return Success (no leak). NO email enqueued. NO new token persisted.
///   - Email known, budget available → mint a 32-byte URL-safe-base64
///     opaque token, persist SHA-256(token), enqueue an outbox event so
///     T-0028/T-0029 can email the raw token. TTL 15 min, single-use.
///
/// Returns Success in every path so the caller cannot use response
/// shape / latency to enumerate emails. The handler implements
/// <see cref="IPersistOnFailureCommand"/> as defense-in-depth: even when
/// a future change introduces a failure return, any per-user state
/// (e.g. a future rate-limit bucket) persists.
/// </summary>
public static class RequestMagicLink
{
    /// <summary>How long a magic-link token is valid. ADR 0012 §Magic link.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Per-email request budget — 3 requests per 10 minutes.</summary>
    public static readonly int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    /// <summary>Outbox event type emitted on success (consumed by T-0029).</summary>
    public const string OutboxEventType = "auth.magicLink.send";

    public sealed record Command(
        string Email,
        string? IpAddress) : ICommand, IPersistOnFailureCommand;

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

            // 1. Resolve the user (includes soft-deleted rows per T-0020 fix).
            //    Both "no such email" and "soft-deleted" exit silently with
            //    Success so the caller can't enumerate.
            var user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);
            if (user is null || !user.IsActive)
            {
                logger.LogInformation("Magic-link request for {EmailNormalized}: no active user; noop.", emailNormalized);
                return BusinessResult.Success();
            }

            // 2. Per-user budget. The window starts MaxRequestsPerWindow
            //    requests ago — any further request inside the window is
            //    a silent no-op.
            var since = now - RateLimitWindow;
            var recent = await tokens.CountIssuedSinceAsync(
                user.Id, OneTimeTokenPurpose.MagicLink, since, cancellationToken);
            if (recent >= MaxRequestsPerWindow)
            {
                logger.LogInformation(
                    "Magic-link request for {UserId} silently dropped: {RecentCount} requests in the last {Window} min.",
                    user.Id, recent, RateLimitWindow.TotalMinutes);
                return BusinessResult.Success();
            }

            // 3. Mint the token. Raw value emailed; only hash persisted.
            var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var expiresAt = now + TokenLifetime;
            tokens.Add(OneTimeToken.Issue(
                tokenHash: hash,
                userId: user.Id,
                purpose: OneTimeTokenPurpose.MagicLink,
                expiresAt: expiresAt,
                now: now,
                ipAddress: command.IpAddress));

            // 4. Enqueue the email. The outbox row commits in the same UoW
            //    as the token row so we never email a token that isn't in
            //    the DB (or vice-versa). The raw token MUST NOT appear in
            //    any log; the outbox payload is the only place it lives
            //    server-side, and the T-0014 SensitivePropertyMasker
            //    redacts it because "token" is on the redaction list.
            var payload = JsonSerializer.Serialize(
                new OutboxPayload(user.Id, user.Email, raw, expiresAt));
            outbox.Enqueue(user.Id, OutboxEventType, payload);

            return BusinessResult.Success();
        }
    }
}
