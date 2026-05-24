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

    /// <summary>
    /// Atomically mark a token consumed iff it is still redeemable
    /// (exists, not previously consumed, not yet expired). Returns true
    /// on success — the caller can now mint the side-effect — or false
    /// if a concurrent request already claimed it. Per T-0023 security
    /// review M-1: prevents a double-click on a magic-link email from
    /// minting two sessions because EF's change-tracker can't serialize
    /// the read-then-write across concurrent handlers.
    ///
    /// Writes immediately, OUTSIDE the UnitOfWork commit. Caller must
    /// then re-read the token (now visible as consumed) to obtain the
    /// associated <see cref="OneTimeToken.UserId"/> / <see cref="OneTimeToken.Purpose"/>.
    /// </summary>
    Task<bool> TryConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(OneTimeToken token);
}
