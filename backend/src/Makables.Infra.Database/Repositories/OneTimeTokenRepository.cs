using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Repositories;

public sealed class OneTimeTokenRepository(MakablesDbContext db) : IOneTimeTokenRepository
{
    public Task<OneTimeToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return Task.FromResult<OneTimeToken?>(null);
        return db.Set<OneTimeToken>().FirstOrDefaultAsync(t => t.Id == tokenHash, cancellationToken);
    }

    public Task<int> CountIssuedSinceAsync(
        string userId,
        OneTimeTokenPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult(0);
        return db.Set<OneTimeToken>()
            .CountAsync(t => t.UserId == userId
                          && t.Purpose == purpose
                          && t.CreatedAt >= since,
                cancellationToken);
    }

    public async Task InvalidateRedeemableAsync(
        string userId,
        OneTimeTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        // Load tracked so the audit interceptor and the UoW commit cover
        // the mutation. The volumes are tiny (one user, one purpose,
        // at most a handful of unconsumed tokens) so a bulk UPDATE isn't
        // worth the complexity here.
        var tokens = await db.Set<OneTimeToken>()
            .Where(t => t.UserId == userId
                     && t.Purpose == purpose
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Consume(now);
        }
    }

    public async Task<bool> TryConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHash)) return false;

        // Conditional UPDATE: only flip ConsumedAt when the row is still
        // redeemable. ExecuteUpdateAsync runs in a single SQL statement
        // so two concurrent requests race in the DB, not in EF's change
        // tracker — exactly one returns affected-rows = 1. Writes
        // immediately, outside the UnitOfWork commit; that's intentional
        // because the UoW only fires on handler completion which is
        // exactly the race window we need to close. Per T-0023 review M-1.
        var affected = await db.Set<OneTimeToken>()
            .Where(t => t.Id == tokenHash
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        return affected == 1;
    }

    public void Add(OneTimeToken token) => db.Set<OneTimeToken>().Add(token);
}
