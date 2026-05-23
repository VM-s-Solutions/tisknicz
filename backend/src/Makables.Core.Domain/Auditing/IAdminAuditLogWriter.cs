namespace Makables.Core.Domain.Auditing;

/// <summary>
/// Snapshot + persistence boundary for the admin audit log. Implemented
/// in <c>Infra.Database</c>; per ADR 0014, the only place audit rows are
/// inserted.
///
/// Lives in <c>Core.Domain</c> rather than <c>Core.AppServices</c>
/// because <c>Infra.Database</c>'s impl needs it, and <c>Infra.*</c>
/// does not reference <c>Core.AppServices</c> per ADR 0001
/// (same rationale as <see cref="Common.IUserSessionProvider"/>).
/// </summary>
public interface IAdminAuditLogWriter
{
    /// <summary>
    /// Returns a JSONB-serializable string snapshot of the target entity
    /// (sensitive fields redacted), or null if the entity does not exist
    /// (e.g. before a create command runs).
    /// </summary>
    Task<string?> CaptureSnapshotAsync(
        string targetEntity,
        string targetId,
        CancellationToken cancellationToken);

    /// <summary>Append an audit log entry. The behavior MUST NOT call this on a failed command.</summary>
    Task AppendAsync(AdminAuditLogEntry entry, CancellationToken cancellationToken);
}
