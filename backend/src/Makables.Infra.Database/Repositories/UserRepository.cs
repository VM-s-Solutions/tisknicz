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

    public Task<User?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized)) return Task.FromResult<User?>(null);
        return db.Set<User>().FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, cancellationToken);
    }

    public Task<User?> GetByGoogleSubAsync(string googleSub, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(googleSub)) return Task.FromResult<User?>(null);
        return db.Set<User>().FirstOrDefaultAsync(u => u.GoogleSub == googleSub, cancellationToken);
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
