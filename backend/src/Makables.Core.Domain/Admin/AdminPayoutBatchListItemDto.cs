using Makables.Core.Domain.Payouts;

namespace Makables.Core.Domain.Admin;

/// <summary>
/// Privileged admin payout-batch list row (T-0127 / Q-0029 /
/// US-admin-0007). The cross-maker, <c>Unscoped()</c> browse list that
/// replaces the T-0118c count + by-id surface — the payout page browses
/// Processing/Completed batches and completes / downloads CSV by VISIBLE
/// id. Distinct from the maker-scoped <c>IPayoutQueries</c> (T-0112)
/// which is per-maker; this list spans every maker.
///
/// <para>
/// Lives in <c>Core.Domain.Admin</c> so the read-side
/// <see cref="IAdminQueries"/> seam references it without a
/// Domain→AppServices layering violation (ADR-0023 precedent).
/// </para>
/// </summary>
public sealed record AdminPayoutBatchListItemDto(
    string BatchId,
    string BatchNumber,
    PayoutBatchState State,
    long TotalAmountMinor,
    string Currency,
    int OrderCount,
    int MakerCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
