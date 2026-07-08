namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Why a <see cref="PayoutDeduction"/> row exists — a negative line item
/// against a maker's next payout batch. T-0146 introduces the first
/// reason; the enum leaves room for future deduction classes without a
/// schema change. Explicit <c>: short</c> backing per the
/// <see cref="Orders.DisputeCategory"/> precedent — new reasons APPEND.
/// </summary>
public enum PayoutDeductionReason : short
{
    /// <summary>
    /// The maker-borne cost of a T-0146 reverse (customer→maker) return
    /// shipment, per dopady §2.5/Q9 — the maker bears the cost of a
    /// return once a dispute is confirmed to warrant one. Cost basis is
    /// whatever Packeta's response gives at label-creation time, or
    /// <c>CountryConfiguration.DefaultShippingPriceMinor</c> as a stand-in
    /// when Packeta doesn't itemize the reverse leg (Q-0037 resolution).
    /// </summary>
    ReturnShippingCost = 0,
}
