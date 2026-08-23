namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// One point on the platform-revenue time series (T-0192) — the same money
/// lines <see cref="PlatformRevenueDto"/> reports for a whole window, but
/// for a single bucket of it. <b>Unscoped, admin host only</b>, for exactly
/// the reason the window aggregate is: it sums across every maker and
/// customer (ADR 0013 puts that boundary on the host audience).
///
/// <para>
/// Recognition is identical to the window aggregate — <c>PaidAt</c>, the
/// moment the money cleared — so a series summed over its buckets equals
/// the single-number read for the same span. That equality is the point:
/// the chart and the tiles must never disagree.
/// </para>
///
/// <para>
/// <see cref="BucketStart"/> is the truncated instant the bucket opens,
/// computed in the country's civil timezone (a "day" is a Prague day, not
/// a UTC day) and returned as the equivalent UTC instant. Buckets are
/// half-open <c>[start, nextStart)</c>, so no order is counted twice.
/// The read side returns only buckets that actually contain a paid order;
/// filling the empty ones is the caller's job.
/// </para>
/// </summary>
/// <param name="BucketStart">Instant the bucket opens (inclusive).</param>
/// <param name="PaidOrderCount">Orders whose payment cleared inside the bucket and was not reversed.</param>
/// <param name="GrossVolumeMinor">Sum of <c>TotalAmountMinor</c> — what customers were charged.</param>
/// <param name="PlatformFeeMinor">Sum of <c>PlatformFeeAmountMinor</c> — what the platform earned.</param>
/// <param name="MakerPayoutMinor">Sum of <c>MakerPayoutAmountMinor</c> — what the makers are owed.</param>
/// <param name="RefundedMinor">Gross refunded on orders paid in the bucket — NOT netted into the fee.</param>
public sealed record PlatformRevenueBucketDto(
    DateTimeOffset BucketStart,
    int PaidOrderCount,
    long GrossVolumeMinor,
    long PlatformFeeMinor,
    long MakerPayoutMinor,
    long RefundedMinor);
