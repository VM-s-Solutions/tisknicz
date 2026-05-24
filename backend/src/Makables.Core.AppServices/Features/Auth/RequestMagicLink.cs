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
///   - Email unknown / soft-deleted → return Success (no leak).
///   - Email known, request budget exhausted ("3 per 10 minutes") →
///     return Success (no leak).
///   - Email known, budget available → mint a 32-byte URL-safe-base64
///     opaque token, persist SHA-256(token), enqueue an outbox event so
///     T-0028/T-0029 can email the raw token. TTL 15 min, single-use.
///
/// Returns Success in every path so the caller cannot use response
/// shape to enumerate emails. The handler also runs the same expensive
/// operations (CSPRNG mint + SHA-256 + JSON serialize + a CountIssued
/// round-trip) on the no-op branches so total LATENCY does not differ
/// by enumeration — per T-0023 security review BLOCKER B-1.
///
/// Implements <see cref="IPersistOnFailureCommand"/> as defense-in-depth:
/// even when a future change introduces a failure return, any per-user
/// state (e.g. a future rate-limit bucket) persists.
/// </summary>
public static class RequestMagicLink
{
    /// <summary>How long a magic-link token is valid. ADR 0012 §Magic link.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Per-email request budget — 3 requests per 10 minutes.</summary>
    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);

    /// <summary>Outbox event type emitted on success (consumed by T-0029).</summary>
    public const string OutboxEventType = "auth.magicLink.send";

    public sealed record Command(
        string Email,
        string? IpAddress) : ICommand, IPersistOnFailureCommand;

    /// <summary>
    /// Outbox payload. The <c>RawToken</c> property name is intentional —
    /// the T-0014 <c>SensitivePropertyMasker</c> pattern list includes
    /// the bare substring "token", so any Serilog scope or EF SQL trace
    /// capturing the serialized payload property-by-property would
    /// redact it. The masker is exercised by the
    /// <c>SensitivePropertyMaskerTests</c> integration test to keep this
    /// contract honest.
    /// </summary>
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
        // Sentinel user id used for the rate-limit round-trip when no
        // user exists. Identical SQL shape and cost as the known-user
        // case; no row will match this id, so the count is 0.
        private const string NoSuchUserSentinel = "__no-such-user__";

        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var now = clock.UtcNow;
            var emailNormalized = User.NormalizeEmail(command.Email);

            // 1. Resolve the user (includes soft-deleted rows per T-0020 fix).
            var user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);

            // 2. Always count recent issuance so the round-trip cost is
            //    constant whether the email is real or not. Per T-0023
            //    security review B-1 — closes the timing channel.
            var rateLimitUserId = user?.Id ?? NoSuchUserSentinel;
            var since = now - RateLimitWindow;
            var recent = await tokens.CountIssuedSinceAsync(
                rateLimitUserId, OneTimeTokenPurpose.MagicLink, since, cancellationToken);

            // 3. Always mint + serialize so the CSPRNG + SHA-256 + JSON
            //    cost is paid on every call. Discarded on no-op branches.
            var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();
            var expiresAt = now + TokenLifetime;
            var payloadJson = JsonSerializer.Serialize(
                new OutboxPayload(user?.Id ?? string.Empty, user?.Email ?? string.Empty, raw, expiresAt));

            // 4. Decide whether to actually persist + enqueue.
            var willSend = user is not null
                        && user.IsActive
                        && recent < MaxRequestsPerWindow;

            if (!willSend)
            {
                logger.LogInformation("Magic-link request for {EmailNormalized}: silent no-op.", emailNormalized);
                return BusinessResult.Success();
            }

            tokens.Add(OneTimeToken.Issue(
                tokenHash: hash,
                userId: user!.Id,
                purpose: OneTimeTokenPurpose.MagicLink,
                expiresAt: expiresAt,
                now: now,
                ipAddress: command.IpAddress));

            // The outbox row commits in the same UoW as the token row so
            // we never email a token that isn't in the DB or vice versa.
            outbox.Enqueue(user.Id, OutboxEventType, payloadJson);

            return BusinessResult.Success();
        }
    }
}
