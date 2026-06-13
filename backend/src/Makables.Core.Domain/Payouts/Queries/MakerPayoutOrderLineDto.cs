namespace Makables.Core.Domain.Payouts.Queries;

/// <summary>
/// One per-order line of the maker payout-batch detail breakdown (T-0112 /
/// US-maker-0012 AC-3). Reconciliation invariant (per
/// <c>PricingBreakdown</c>):
/// <c>ProductPriceMinor − PlatformFeeAmountMinor + ShippingPriceMinor ==
/// MakerPayoutAmountMinor</c>, and
/// <c>SUM(line.MakerPayoutAmountMinor) == MakerPayoutDetailDto.MakerTotalPaidMinor</c>.
/// Customer email / address are deliberately NOT projected (T-0081 §A.2 GDPR
/// lock — the breakdown is money, not contact).
/// </summary>
public sealed record MakerPayoutOrderLineDto(
    string OrderId,
    string OrderNumber,
    long ProductPriceMinor,
    long ShippingPriceMinor,
    long PlatformFeeAmountMinor,
    long MakerPayoutAmountMinor,
    string Currency);
