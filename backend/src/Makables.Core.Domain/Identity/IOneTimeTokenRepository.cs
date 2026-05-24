namespace Makables.Core.Domain.Identity;

/// <summary>
/// Persistence boundary for <see cref="OneTimeToken"/>. Implementation in
/// <c>Makables.Infra.Database.Repositories</c>.
///
/// All lookups go by token hash (PK probe). Per-user / per-purpose
/// helpers exist so the rate-limit and "invalidate any prior token"
/// flows in T-0023 / T-0025 don't have to expose <see cref="OneTimeToken.Id"/>
/// scanning to handlers.
/// </summary>
public interface IOneTimeTokenRepository
{
    /// <summary>
    /// Resolve by SHA-256 hex hash of the raw token. Returns tracked.
    /// </summary>
    Task<OneTimeToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Count tokens issued for <paramref name="userId"/> of
    /// <paramref name="purpose"/> created at or after <paramref name="since"/>.
    /// Used by the per-email rate limit ("3 magic-link requests per email
    /// per 10 minutes" per ADR 0012 §Magic link).
    /// </summary>
    Task<int> CountIssuedSinceAsync(
        string userId,
        OneTimeTokenPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark every still-redeemable token of <paramref name="purpose"/>
    /// for <paramref name="userId"/> as consumed. Called by the password-
    /// reset request flow so a prior link can't be replayed after a new
    /// one has been issued.
    /// </summary>
    Task InvalidateRedeemableAsync(
        string userId,
        OneTimeTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void Add(OneTimeToken token);
}
