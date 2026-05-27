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
}
