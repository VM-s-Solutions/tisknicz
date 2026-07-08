using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class UserRepository(MakablesDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult<User?>(null);
        return db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public Task<User?> GetByIdIgnoringFiltersAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult<User?>(null);
        // IgnoreQueryFilters: GDPR erasure (T-0110) must reach a
        // soft-deleted/deactivated user — a deactivation does not satisfy a
        // right-to-erasure request (ADR 0013 §"Hard delete (GDPR)").
        return db.Set<User>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public Task<User?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized)) return Task.FromResult<User?>(null);
        // IgnoreQueryFilters: login / password-reset / magic-link / OAuth
        // linking must be able to resolve a soft-deleted account so the
        // service layer can produce the correct user-facing error
        // ("account deactivated") and so the lockout-by-email symmetry
        // from ADR 0012 §Lockout stays intact. Reviewer T-0020 BLOCKER B-1.
        return db.Set<User>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, cancellationToken);
    }

    public Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(googleSub)) return Task.FromResult<User?>(null);
        return db.Set<User>().FirstOrDefaultAsync(u => u.GoogleSub == googleSub, cancellationToken);
    }

    public Task<User?> GetByAppleSubAsync(string appleSub, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appleSub)) return Task.FromResult<User?>(null);
        return db.Set<User>().FirstOrDefaultAsync(u => u.AppleSub == appleSub, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized)) return Task.FromResult(false);
        // IgnoreQueryFilters so a soft-deleted account's email still blocks
        // re-registration. See ADR 0013 — the email is a stable identifier
        // and re-use is opt-in only via an admin purge.
        return db.Set<User>()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EmailNormalized == emailNormalized, cancellationToken);
    }

    public void Add(User user) => db.Set<User>().Add(user);
}
