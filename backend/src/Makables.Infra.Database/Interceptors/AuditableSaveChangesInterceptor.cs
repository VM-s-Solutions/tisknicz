using Makables.Core.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Makables.Infra.Database.Interceptors;

/// <summary>
/// Populates <see cref="Auditable.CreatedBy"/>/<c>CreatedAt</c> on Added
/// entities and <see cref="Auditable.UpdatedBy"/>/<c>UpdatedAt</c> on
/// Modified entities by reading from <see cref="IUserSessionProvider"/>
/// and <see cref="IClock"/>. Per ADR 0014 / patterns §A.11.
///
/// Note on soft delete: this interceptor does NOT auto-flip
/// <c>IsActive</c> on EF <c>Remove</c>. Domain code calls
/// <see cref="Auditable.MarkDeactivated"/> explicitly when a soft delete
/// is the intent. EF <c>Remove</c> remains a hard delete — used only by
/// the GDPR <c>DeleteUserPermanently</c> path (T-0110).
/// </summary>
public sealed class AuditableSaveChangesInterceptor(
    IUserSessionProvider userSessionProvider,
    IClock clock)
    : SaveChangesInterceptor
{
    private const string SystemActor = "system";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            StampAuditFields(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            StampAuditFields(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void StampAuditFields(DbContext context)
    {
        var now = clock.UtcNow;
        var actor = userSessionProvider.GetUserId() ?? SystemActor;

        foreach (var entry in context.ChangeTracker.Entries<Auditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Only stamp if not already set (lets seed migrations and
                    // tests supply explicit values via MarkCreated).
                    if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
                    {
                        entry.Entity.MarkCreated(actor, now);
                    }
                    break;

                case EntityState.Modified:
                    entry.Entity.MarkUpdated(actor, now);
                    break;
            }
        }
    }
}
