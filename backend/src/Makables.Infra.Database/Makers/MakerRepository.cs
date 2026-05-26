using Makables.Core.Domain.Makers;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Makers;

/// <summary>
/// EF Core <see cref="IMakerRepository"/> impl. Tracked reads on
/// <see cref="GetByUserIdAsync"/> because admin commands (T-0034)
/// mutate the returned aggregate and rely on the
/// <c>UnitOfWorkPipelineBehavior</c> to commit.
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
