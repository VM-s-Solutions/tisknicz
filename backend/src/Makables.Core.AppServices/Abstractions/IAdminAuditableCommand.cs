namespace Makables.Core.AppServices.Abstractions;

/// <summary>
/// Marker for an admin write command that must be recorded in
/// <c>admin_audit_log</c>. The <c>AdminAuditPipelineBehavior</c> wraps any
/// command implementing this interface and captures a before/after JSONB
/// snapshot per ADR 0014.
///
/// Implemented by every Web.Admin command. PRs adding a new admin write
/// without this marker are rejected by the reviewer.
/// </summary>
public interface IAdminAuditableCommand : ICommandMarker
{
    /// <summary>Dot-notation, e.g. "maker.verify".</summary>
    string ActionCode { get; }

    /// <summary>Entity name in lowercase, e.g. "maker", "order".</summary>
    string TargetEntity { get; }

    /// <summary>Id of the entity being acted upon.</summary>
    string TargetId { get; }

    /// <summary>Optional free-text admin note captured at command time.</summary>
    string? Notes { get; }
}
