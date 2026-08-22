namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Platform earnings over one reporting window (T-0182). Aggregate read —
/// <b>unscoped, admin host only</b>: it sums money across every maker and
/// customer, so it must never be reachable from the customer/maker hosts
/// (ADR 0013 puts that boundary on the host audience).
///
/// <para>
/// Revenue is recognised at <see cref="Order.PaidAt"/> — the moment the
/// money actually cleared — not at <c>CreatedAt</c> (an order that never
/// gets paid earned nothing) and not at <c>CompletedAt</c> (payout
/// settlement runs weeks later and would make "today" always read zero).
/// Every amount is a snapshot column on the order, so a historical window
/// still reconciles after a commission-rate change.
/// </para>
///
/// <para>
/// <see cref="RefundedMinor"/> is reported as its own line rather than
/// netted into <see cref="PlatformFeeMinor"/>: the refund columns record
/// the gross amount returned to the customer, which does not decompose
/// into a fee share and a payout share. Netting it would silently
/// understate commission by the maker's portion. The operator sees both
/// numbers and the deduction stays honest.
/// </para>
/// </summary>
/// <param name="PaidOrderCount">Orders whose payment cleared inside the window and was not reversed.</param>
/// <param name="GrossVolumeMinor">Sum of <c>TotalAmountMinor</c> — what customers were charged.</param>
/// <param name="PlatformFeeMinor">Sum of <c>PlatformFeeAmountMinor</c> — what the platform earned. The headline number.</param>
/// <param name="MakerPayoutMinor">Sum of <c>MakerPayoutAmountMinor</c> — what the makers are owed.</param>
/// <param name="RefundedMinor">
/// Gross amount refunded on orders paid in the window, including partial refunds on
/// still-live orders and orders that ended <see cref="OrderState.Refunded"/>.
/// </param>
/// <param name="Currency">ISO 4217 code of the window. Czech-only at launch, so always <c>CZK</c>.</param>
public sealed record PlatformRevenueDto(
    int PaidOrderCount,
    long GrossVolumeMinor,
    long PlatformFeeMinor,
    long MakerPayoutMinor,
    long RefundedMinor,
    string Currency);
