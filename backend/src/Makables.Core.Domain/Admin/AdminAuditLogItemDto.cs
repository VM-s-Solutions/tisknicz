namespace Makables.Core.Domain.Admin;

/// <summary>
/// Admin audit-log list row (T-0111 / US-admin-0015). DELIBERATELY omits
/// <c>BeforeJson</c>/<c>AfterJson</c> — the side-by-side diff is a
/// detail-page concern (US-admin-0015 AC-2, out of scope); shipping the
/// JSONB blobs in every list row bloats the payload and leaks sensitive
/// before/after values into a view that doesn't render them.
/// </summary>
public sealed record AdminAuditLogItemDto(
    string Id,
    string AdminUserId,
    string ActionCode,
    string TargetEntity,
    string TargetId,
    string? Notes,
    string? IpAddress,
    DateTimeOffset CreatedAt);
