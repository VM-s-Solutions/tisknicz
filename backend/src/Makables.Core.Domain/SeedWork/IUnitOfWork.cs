namespace Makables.Core.Domain.SeedWork;

/// <summary>
/// Persistence boundary. The <c>UnitOfWorkPipelineBehavior</c> in
/// <c>Core.AppServices</c> calls <see cref="SaveChangesAsync"/> automatically
/// after a successful command. Handlers MUST NOT call it directly
/// (patterns §A.5).
///
/// Implemented by <c>MakablesDbContext</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
