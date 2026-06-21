using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Makables.Infra.Database.Auditing;

/// <summary>
/// T-0137 (Q-0028) read-side audit writer. Persists one
/// <see cref="AdminAuditLogEntry"/> per privileged admin PII read on a
/// DEDICATED <see cref="MakablesDbContext"/> obtained from
/// <see cref="IDbContextFactory{TContext}"/>.
///
/// <para>
/// The own-context commit is deliberate (the T-0032 ARES-cache precedent):
/// a pure read must not open the request-scoped UnitOfWork or call
/// <c>SaveChangesAsync</c> from a handler, and the audit write must not be
/// able to flush a caller's tracked-but-uncommitted aggregates. A fresh
/// context, a single <c>Add</c>, and one <c>SaveChangesAsync</c> keep the
/// audit row fully isolated from the read path.
/// </para>
/// </summary>
public sealed class AdminReadAuditWriter(
    IDbContextFactory<MakablesDbContext> contextFactory,
    IUserSessionProvider session,
    IClock clock,
    IIdGenerator idGenerator) : IAdminReadAuditWriter
{
    public async Task AuditReadAsync(
        string actionCode,
        string targetEntity,
        string targetId,
        string? ipAddress,
        string? userAgent,
        string? notes,
        CancellationToken cancellationToken)
    {
        // Reads have no state delta — before/after snapshots are null.
        var entry = AdminAuditLogEntry.Record(
            id: idGenerator.Next(),
            adminUserId: session.GetUserId() ?? "system",
            actionCode: actionCode,
            targetEntity: targetEntity,
            targetId: targetId,
            beforeJson: null,
            afterJson: null,
            now: clock.UtcNow,
            notes: notes,
            ipAddress: ipAddress,
            userAgent: userAgent);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Set<AdminAuditLogEntry>().Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
