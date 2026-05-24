using System.Text.Json;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Auth;

/// <summary>
/// Reusable "issue an opaque single-use token by email" pipeline. Per
/// ADR 0012 §Magic link / §Email confirmation / §Password reset — all
/// three flows share the same shape:
///   1. Resolve the user.
///   2. Pay the per-user rate-limit round-trip (even on no-op).
///   3. Pay the CSPRNG + SHA-256 + JSON serialize cost (even on no-op).
///   4. Decide whether to actually emit, via an eligibility predicate
///      (every flow checks active + budget; some add extra clauses like
///      "email not yet confirmed" or "no prior reset token").
///   5. Optional pre-issue side effect (password reset invalidates prior
///      tokens before minting the new one).
///   6. Persist the token + enqueue the outbox event in the same UoW.
///
/// Steps 2 and 3 are the timing-equalization invariant per T-0023
/// security review BLOCKER B-1 — owning them in one place keeps that
/// invariant from regressing across N copies.
/// </summary>
public interface IOneTimeTokenIssuer
{
    Task<IssueOutcome> IssueAsync(IssueRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Issuance parameters. The variadic part of the pattern lives in
/// <see cref="EligibilityFilter"/> (extra "don't send" conditions on
/// the user) and <see cref="PreIssueHook"/> (e.g. invalidate prior
/// tokens before minting).
/// </summary>
public sealed record IssueRequest(
    string Email,
    OneTimeTokenPurpose Purpose,
    TimeSpan TokenLifetime,
    string OutboxEventType,
    int MaxRequestsPerWindow,
    TimeSpan RateLimitWindow,
    string? IpAddress = null,
    Func<User, bool>? EligibilityFilter = null,
    Func<User, DateTimeOffset, CancellationToken, Task>? PreIssueHook = null);

/// <summary>
/// Reports what happened. Handlers that just want the contract
/// (i.e. "I always return Success to deny enumeration") can ignore
/// the outcome; the few that care (e.g. Register, which logs differently
/// based on whether an email actually went out) inspect it.
/// </summary>
public sealed record IssueOutcome(bool Sent, string? UserId);

public sealed class OneTimeTokenIssuer(
    IUserRepository users,
    IOneTimeTokenRepository tokens,
    IOutbox outbox,
    IClock clock,
    ILanguageResolver languageResolver,
    ILogger<OneTimeTokenIssuer> logger) : IOneTimeTokenIssuer
{
    // Sentinel user id used for the rate-limit round-trip when no user
    // exists. Identical SQL shape and cost as the known-user case; no
    // row will match this id, so the count is always 0.
    private const string NoSuchUserSentinel = "__no-such-user__";

    // Sentinel User instance used to drive the language resolver on the
    // no-user branch, so it pays the same country-lookup roundtrip as
    // the known-user branch. The resolved value is discarded (no email
    // is sent). Country is the launch market — keeps the country-lookup
    // SQL shape identical and the cache hot.
    private static readonly User UnknownUserProbe = User.Create(
        id: NoSuchUserSentinel,
        email: "__no-such-user__@invalid.local",
        role: UserRole.Customer,
        fullName: "__no-such-user__",
        countryCodePrimary: LanguageCode.DefaultFallback[3..]);

    public async Task<IssueOutcome> IssueAsync(IssueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var emailNormalized = User.NormalizeEmail(request.Email);

        var user = await users.GetByEmailNormalizedAsync(emailNormalized, cancellationToken);

        // Step 2: rate-limit round-trip — paid unconditionally so an
        // attacker can't enumerate emails by latency.
        var rateLimitUserId = user?.Id ?? NoSuchUserSentinel;
        var since = now - request.RateLimitWindow;
        var recent = await tokens.CountIssuedSinceAsync(
            rateLimitUserId, request.Purpose, since, cancellationToken);

        // Step 3: mint + serialize — paid unconditionally.
        var (raw, hash) = OpaqueTokenFactory.GenerateUrlSafe32();
        var expiresAt = now + request.TokenLifetime;
        // Resolve language unconditionally so the no-user branch pays the
        // same DB-roundtrip cost as the known-user branch (timing-equalization
        // invariant T-0023 B-1). For the no-user case we use a sentinel User
        // with the launch country and no preferred language — the resolver
        // performs the same country lookup the known-user case does, but
        // the value is unused because no email will be sent.
        var languageProbe = user ?? UnknownUserProbe;
        var languageCode = await languageResolver.ResolveForUserAsync(
            languageProbe, cancellationToken);
        var payloadJson = JsonSerializer.Serialize(
            new OneTimeTokenOutboxPayload(
                UserId: user?.Id ?? string.Empty,
                Email: user?.Email ?? string.Empty,
                RawToken: raw,
                ExpiresAt: expiresAt,
                LanguageCode: languageCode));

        // Step 4: eligibility.
        var willSend = user is not null
                    && user.IsActive
                    && recent < request.MaxRequestsPerWindow
                    && (request.EligibilityFilter is null || request.EligibilityFilter(user));

        if (!willSend)
        {
            logger.LogInformation(
                "OneTimeToken {Purpose} for {EmailNormalized}: silent no-op.",
                request.Purpose, emailNormalized);
            return new IssueOutcome(Sent: false, UserId: user?.Id);
        }

        // Step 5: pre-issue hook (e.g. invalidate prior reset tokens).
        if (request.PreIssueHook is not null)
        {
            await request.PreIssueHook(user!, now, cancellationToken);
        }

        // Step 6: persist + enqueue.
        tokens.Add(OneTimeToken.Issue(
            tokenHash: hash,
            userId: user!.Id,
            purpose: request.Purpose,
            expiresAt: expiresAt,
            now: now,
            ipAddress: request.IpAddress));

        outbox.Enqueue(user.Id, request.OutboxEventType, payloadJson);

        return new IssueOutcome(Sent: true, UserId: user.Id);
    }
}
