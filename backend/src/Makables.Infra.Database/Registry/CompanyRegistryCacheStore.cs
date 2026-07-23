using Makables.Core.Domain.Registry;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Registry;

/// <summary>
/// EF Core <see cref="ICompanyRegistryCacheStore"/> impl per T-0032 sec
/// reviewer M-1. Each call constructs a fresh
/// <see cref="MakablesDbContext"/> via <see cref="IDbContextFactory{TContext}"/>
/// so cache reads / writes are completely isolated from the calling
/// command's request-scoped DbContext. The implication: a handler that
/// invokes <c>ICompanyRegistry.LookupByRegistrationNumberAsync</c>
/// mid-command cannot have its tracked-but-uncommitted aggregates
/// accidentally flushed by the cache write.
///
/// Tracked reads are still used inside this store so a row can be
/// updated in place rather than via DELETE+INSERT.
/// </summary>
public sealed class CompanyRegistryCacheStore(
    IDbContextFactory<MakablesDbContext> dbContextFactory)
    : ICompanyRegistryCacheStore
{
    public async Task<CompanyRegistryCacheEntry?> GetAsync(
        string registryCode,
        string registrationNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registryCode) || string.IsNullOrWhiteSpace(registrationNumber))
            return null;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<CompanyRegistryCacheEntry>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.RegistryCode == registryCode && e.RegistrationNumber == registrationNumber,
                cancellationToken);
    }

    public async Task UpsertAsync(
        string registryCode,
        string registrationNumber,
        string payloadJson,
        DateTimeOffset fetchedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        // T-0032 Copilot review: atomic upsert via "ON CONFLICT DO UPDATE"
        // so two concurrent lookups for the same (registry, IČO) don't both
        // observe `existing is null` and then race on INSERT — the loser
        // would otherwise bubble a DbUpdateException out of the adapter
        // and break the "no exceptions cross the boundary" contract.
        //
        // Both Postgres and SQLite (used by some integration tests) support
        // ON CONFLICT … DO UPDATE with identical EXCLUDED semantics — but
        // the payload column is `jsonb` on Postgres and a text parameter is
        // NOT implicitly cast in VALUES (42804 "column is of type jsonb but
        // expression is of type text" — T-0160, found live on dev by the
        // T-0153 walk; the SQLite harness degrades jsonb to TEXT, which is
        // why no test ever hit it). Postgres therefore needs the explicit
        // ::jsonb cast; SQLite must NOT get it (no cast-to-jsonb syntax).
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO company_registry_cache
                    (registry_code, registration_number, payload, fetched_at, expires_at)
                VALUES
                    ({registryCode}, {registrationNumber}, {payloadJson}::jsonb, {fetchedAt}, {expiresAt})
                ON CONFLICT (registry_code, registration_number) DO UPDATE SET
                    payload = EXCLUDED.payload,
                    fetched_at = EXCLUDED.fetched_at,
                    expires_at = EXCLUDED.expires_at
            ", cancellationToken);
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO company_registry_cache
                (registry_code, registration_number, payload, fetched_at, expires_at)
            VALUES
                ({registryCode}, {registrationNumber}, {payloadJson}, {fetchedAt}, {expiresAt})
            ON CONFLICT (registry_code, registration_number) DO UPDATE SET
                payload = EXCLUDED.payload,
                fetched_at = EXCLUDED.fetched_at,
                expires_at = EXCLUDED.expires_at
        ", cancellationToken);
    }

    public async Task<int> EvictFetchedBeforeAsync(
        DateTimeOffset fetchedBefore,
        CancellationToken cancellationToken)
    {
        // Raw set-based DELETE in the store's own DbContext scope, mirroring
        // the UpsertAsync raw-SQL approach above. Raw SQL (not a LINQ
        // ExecuteDelete) because the EF Core SQLite provider used in
        // integration tests cannot translate a DateTimeOffset comparison
        // server-side; the parameter binds identically on both providers, and
        // fetched_at is always written as UTC (AresCompanyRegistry uses
        // clock.UtcNow), so the comparison is chronologically correct.
        // ExecuteSqlInterpolatedAsync returns the affected-row count.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM company_registry_cache WHERE fetched_at < {fetchedBefore}",
            cancellationToken);
    }
}
