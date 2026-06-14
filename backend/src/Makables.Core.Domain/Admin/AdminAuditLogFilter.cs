namespace Makables.Core.Domain.Admin;

/// <summary>
/// Filter dimensions for the admin audit log (T-0111 / US-admin-0015
/// AC-1): admin user, action code, target entity, date range — no more
/// (Q-E). <see cref="AdminUserId"/>/<see cref="ActionCode"/>/
/// <see cref="TargetEntity"/> are exact matches;
/// <see cref="DateRangeStart"/>/<see cref="DateRangeEnd"/> compare
/// inclusively against <c>CreatedAt</c>.
/// </summary>
public sealed record AdminAuditLogFilter(
    string? AdminUserId,
    string? ActionCode,
    string? TargetEntity,
    DateTimeOffset? DateRangeStart,
    DateTimeOffset? DateRangeEnd);
