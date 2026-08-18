using Makables.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Identity;

/// <summary>
/// EF Core <see cref="IAuthRetentionStore"/> implementation. Each call takes a
/// fresh <see cref="MakablesDbContext"/> from the factory so the purge is
/// isolated from any ambient request scope — the T-0032/T-0113
/// <c>CompanyRegistryCacheStore</c> precedent, and the reason this is a store
/// rather than a repository: repositories return aggregates inside the request
/// unit of work, this runs set-based deletes from a timer with no request.
/// </summary>
public sealed class AuthRetentionStore(
    IDbContextFactory<MakablesDbContext> dbContextFactory)
    : IAuthRetentionStore
{
    public async Task<AuthRetentionPurgeResult> PurgeExpiredAsync(
        DateTimeOffset expiredBefore,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Raw set-based DELETEs rather than LINQ ExecuteDelete: the SQLite
        // provider the unit tests run on cannot translate a DateTimeOffset
        // comparison server-side, while an interpolated parameter binds
        // identically on both providers (T-0113 precedent). Every column
        // compared here is written from IClock.UtcNow, so the comparison is
        // chronologically sound.
        //
        // Three statements, not one CTE: they are independent, the counts are
        // reported per table, and a partial failure on the second still leaves
        // the first committed — deleting expired junk is idempotent, so a
        // retry simply resumes.
        var refreshTokens = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM refresh_tokens WHERE expires_at < {expiredBefore}",
            cancellationToken);

        var oneTimeTokens = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM one_time_tokens WHERE expires_at < {expiredBefore}",
            cancellationToken);

        // A bucket is anti-abuse state keyed by the email itself. It is only
        // removable once BOTH its last attempt and any lockout predate the
        // cutoff — the lockout leg is redundant at a sane retention window
        // (lockouts last minutes) but makes a misconfigured short window
        // incapable of releasing someone who is currently locked out.
        var loginAttemptBuckets = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM login_attempt_buckets
             WHERE last_attempt_at < {expiredBefore}
               AND (locked_until IS NULL OR locked_until < {expiredBefore})
             """,
            cancellationToken);

        return new AuthRetentionPurgeResult(refreshTokens, oneTimeTokens, loginAttemptBuckets);
    }
}
