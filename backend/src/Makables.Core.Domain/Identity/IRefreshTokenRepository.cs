namespace Makables.Core.Domain.Identity;

/// <summary>
/// Persistence boundary for <see cref="RefreshToken"/>. Implementation in
/// <c>Makables.Infra.Database.Repositories</c>.
///
/// Lookup methods accept the SHA-256 hash of the raw token — callers
/// compute the hash and pass it in; the raw token never reaches this
/// repository per ADR 0012.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Used during reuse detection: when an already-revoked token is
    /// presented, the caller revokes the whole family.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(string familyId, CancellationToken cancellationToken);

    /// <summary>List all currently-active tokens for a user (admin views + logout-all).</summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(string userId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}
