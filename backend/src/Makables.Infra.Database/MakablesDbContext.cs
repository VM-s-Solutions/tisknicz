using System.Linq.Expressions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.SeedWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Makables.Infra.Database;

/// <summary>
/// Application-wide EF Core DbContext. Per ADR 0013 / patterns §A.19,
/// every entity that derives from <see cref="Auditable"/> receives an
/// automatic global query filter on <see cref="BaseEntity.IsActive"/>
/// (soft delete). Country and ownership scoping is enforced at the
/// application layer (repository methods + JWT claims), not via global
/// query filters — see ADR 0013 for the rationale.
///
/// Implements <see cref="IUnitOfWork"/>. The
/// <c>UnitOfWorkPipelineBehavior</c> in <c>Core.AppServices</c> drives
/// commits. Handlers MUST NOT call <see cref="SaveChangesAsync"/>
/// directly.
/// </summary>
public class MakablesDbContext(DbContextOptions<MakablesDbContext> options)
    : DbContext(options), IUnitOfWork
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplyEntityConfigurationsFromAssembly(modelBuilder);
        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Translate a Postgres unique-violation race (SQLSTATE <c>23505</c>)
    /// into <see cref="UniqueConstraintViolationException"/> so the
    /// <c>UnitOfWorkPipelineBehavior</c> can return a typed
    /// <c>BusinessResult</c> failure instead of bubbling a raw
    /// <c>DbUpdateException</c> as a 500. T-0033 reviewer security M-1.
    ///
    /// <para>Any other <c>DbUpdateException</c> (FK violation, deadlock,
    /// etc.) keeps propagating — those are still bugs / infra incidents
    /// and SHOULD surface as 500s for ops visibility.</para>
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx
            && pgEx.SqlState == PostgresErrorCodes.UniqueViolation
            && !string.IsNullOrEmpty(pgEx.ConstraintName))
        {
            throw new UniqueConstraintViolationException(pgEx.ConstraintName, ex);
        }
    }

    /// <summary>
    /// Discover and apply every <see cref="IEntityTypeConfiguration{TEntity}"/>
    /// declared under <c>Makables.Infra.Database/Configurations</c>. Phase 1
    /// has no entities yet; this is forward-wiring for Phase 2+.
    /// </summary>
    private static void ApplyEntityConfigurationsFromAssembly(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MakablesDbContext).Assembly);
    }

    /// <summary>
    /// Apply a global query filter on <c>IsActive</c> for every entity
    /// inheriting <see cref="Auditable"/>. Filter is composed via
    /// <c>Expression.Lambda</c> so it works for any concrete subtype that
    /// lands in Phase 2+. Queries that legitimately need soft-deleted rows
    /// (admin audit views, GDPR reconciliation) opt out with
    /// <c>.IgnoreQueryFilters()</c>; every such call site must be
    /// documented per ADR 0013.
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Auditable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isActiveProperty = Expression.Property(parameter, nameof(BaseEntity.IsActive));
            var filter = Expression.Lambda(isActiveProperty, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
