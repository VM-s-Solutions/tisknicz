using Makables.Core.Domain.Makers;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Makers;

/// <summary>
/// EF Core <see cref="IMakerRepository"/> impl. Tracked reads on
/// <see cref="GetByUserIdAsync"/> because admin commands (T-0034)
/// mutate the returned aggregate and rely on the
/// <c>UnitOfWorkPipelineBehavior</c> to commit.
///
/// <para>
/// Active-row filtering: neither query below adds <c>.Where(m =&gt; m.IsActive)</c>
/// because <c>MakablesDbContext.ApplySoftDeleteQueryFilters</c> attaches
/// a global query filter to every <see cref="Common.Auditable"/> entity.
/// Soft-deleted rows are invisible here by construction, which matches
/// the interface contract (T-0033 cq reviewer M-1: no explicit
/// <c>.IgnoreQueryFilters()</c> in this file, so the filter is active).
/// </para>
/// </summary>
public sealed class MakerRepository(MakablesDbContext db) : IMakerRepository
{
    public void Add(Maker maker)
    {
        ArgumentNullException.ThrowIfNull(maker);
        db.Set<Maker>().Add(maker);
    }

    public Task<bool> IcoExistsAsync(string registrationNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber)) return Task.FromResult(false);
        var trimmed = registrationNumber.Trim();
        return db.Set<Maker>()
            .AnyAsync(m => m.RegistrationNumber == trimmed, cancellationToken);
    }

    public Task<Maker?> GetByUserIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult<Maker?>(null);
        return db.Set<Maker>()
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
    }

    public Task<Maker?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Maker?>(null);
        return db.Set<Maker>()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug)) return Task.FromResult(false);
        var trimmed = slug.Trim();
        return db.Set<Maker>()
            .AnyAsync(m => m.Slug == trimmed, cancellationToken);
    }

    public Task<Maker?> GetByIdForUpdateAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Maker?>(null);

        // SELECT ... FOR UPDATE row-lock held for the surrounding UoW
        // transaction (T-0100 §A.5 — serialize concurrent rating
        // recomputes to the same maker). FromSqlInterpolated bypasses the
        // global soft-delete query filter, so the is_active predicate is
        // restated here to keep the active-only contract (mirrors the
        // NumberingSequenceAllocator FOR UPDATE precedent on a non-tracked
        // raw read; the returned instance IS tracked because the handler
        // mutates it via Maker.RecomputeRating).
        return db.Set<Maker>()
            .FromSqlInterpolated($@"
                SELECT * FROM makers
                WHERE id = {id} AND is_active
                FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
    }
}
