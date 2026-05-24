using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class RefreshTokenRepository(MakablesDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return Task.FromResult<RefreshToken?>(null);
        // IgnoreQueryFilters: we explicitly want to see revoked + expired
        // tokens so reuse detection can fire on them. The caller is
        // responsible for checking IsActiveAt.
        return db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(string familyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(familyId)) return [];
        // IgnoreQueryFilters: refresh tokens are never soft-deleted — their
        // lifecycle is RevokedAt + ExpiresAt. Adding IgnoreQueryFilters is
        // defense-in-depth so a stray IsActive=false (admin tool, bug)
        // can't make a family member vanish during reuse-detection
        // revocation. Reviewer T-0020 MAJOR M-4.
        var list = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return [];
        // IgnoreQueryFilters — see GetActiveByFamilyAsync above.
        var list = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        return list;
    }

    public void Add(RefreshToken token) => db.Set<RefreshToken>().Add(token);
}
