namespace Makables.Core.Domain.Identity;

/// <summary>
/// Persistence boundary for <see cref="User"/>. Implementation lives in
/// <c>Makables.Infra.Database.Repositories</c>; handlers depend on this
/// interface only (ADR 0001 layering).
///
/// Lookup methods return the tracked entity when found so subsequent
/// mutations + UoW commit work naturally.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Load a user by id bypassing the soft-delete query filter
    /// (<c>.IgnoreQueryFilters()</c>). GDPR erasure (T-0110) must reach a
    /// soft-deleted/deactivated user too — a deactivation does NOT satisfy a
    /// right-to-erasure request. Tracked. Returns <c>null</c> for unknown ids.
    /// </summary>
    Task<User?> GetByIdIgnoringFiltersAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Look up by normalized email (see <see cref="User.NormalizeEmail"/>).
    /// Returns the tracked entity if present, INCLUDING soft-deleted rows —
    /// the service layer needs to distinguish "no such account" from
    /// "account deactivated" and ADR 0012 §Lockout requires the lookup to
    /// be deterministic regardless of IsActive.
    /// </summary>
    Task<User?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken);

    /// <summary>Look up by Google <c>sub</c> (for OAuth login).</summary>
    Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken);

    /// <summary>
    /// True if any user (active or soft-deleted) has the given normalized
    /// email. Used at registration to prevent re-using a deactivated
    /// account's email until ops explicitly purges it.
    /// </summary>
    Task<bool> EmailExistsAsync(string emailNormalized, CancellationToken cancellationToken);

    void Add(User user);
}
