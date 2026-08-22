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
    DateTimeOffset? DateRangeEnd,
    /// <summary>
    /// Exact <c>TargetId</c> match (T-0177, audit ADM-H2). The admin
    /// order-detail page used to fetch the GLOBAL <c>targetEntity:'order'</c>
    /// slice and filter it client-side, so on a busy marketplace an order's
    /// audit section could render EMPTY — with pagination reflecting the
    /// global set — while its entries sat on later pages. That surface is
    /// the evidence trail for refund/dispute triage, so an incomplete render
    /// is actively dangerous. Filtering server-side is the fix.
    /// </summary>
    string? TargetId = null);
