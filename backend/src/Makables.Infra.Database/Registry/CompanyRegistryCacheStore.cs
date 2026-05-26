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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Set<CompanyRegistryCacheEntry>()
            .FirstOrDefaultAsync(
                e => e.RegistryCode == registryCode && e.RegistrationNumber == registrationNumber,
                cancellationToken);

        if (existing is null)
        {
            db.Set<CompanyRegistryCacheEntry>().Add(CompanyRegistryCacheEntry.Create(
                registryCode: registryCode,
                registrationNumber: registrationNumber,
                payloadJson: payloadJson,
                fetchedAt: fetchedAt,
                expiresAt: expiresAt));
        }
        else
        {
            existing.Refresh(payloadJson, fetchedAt, expiresAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
