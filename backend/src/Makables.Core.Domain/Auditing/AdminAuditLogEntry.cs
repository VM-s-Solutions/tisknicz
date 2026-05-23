namespace Makables.Core.Domain.Auditing;

/// <summary>
/// Append-only record of an admin write action. Per ADR 0014. Not
/// <see cref="Common.Auditable"/> — this entity IS the audit log; it
/// records who did what to whom, not who edited the audit log entry.
///
/// The underlying table has a Postgres trigger that rejects UPDATE and
/// DELETE statements; the application layer never modifies rows after
/// insertion.
/// </summary>
public sealed class AdminAuditLogEntry
{
    public string Id { get; private set; } = default!;
    public string AdminUserId { get; private set; } = default!;
    public string ActionCode { get; private set; } = default!;
    public string TargetEntity { get; private set; } = default!;
    public string TargetId { get; private set; } = default!;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? Notes { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AdminAuditLogEntry() { }

    public static AdminAuditLogEntry Record(
        string id,
        string adminUserId,
        string actionCode,
        string targetEntity,
        string targetId,
        string? beforeJson,
        string? afterJson,
        DateTimeOffset now,
        string? notes = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id required.", nameof(id));
        if (string.IsNullOrWhiteSpace(adminUserId)) throw new ArgumentException("AdminUserId required.", nameof(adminUserId));
        if (string.IsNullOrWhiteSpace(actionCode)) throw new ArgumentException("ActionCode required.", nameof(actionCode));
        if (string.IsNullOrWhiteSpace(targetEntity)) throw new ArgumentException("TargetEntity required.", nameof(targetEntity));
        if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("TargetId required.", nameof(targetId));

        return new AdminAuditLogEntry
        {
            Id = id,
            AdminUserId = adminUserId,
            ActionCode = actionCode,
            TargetEntity = targetEntity,
            TargetId = targetId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Notes = notes,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = now,
        };
    }
}
