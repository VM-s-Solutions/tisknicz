using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Payouts;

/// <summary>
/// Per-order payout verdict. The repository fetches coarse candidates
/// (Delivered + unbatched + country); this verdict is the fine filter.
/// </summary>
public enum PayoutEligibilityVerdict
{
    /// <summary>Claim this order's maker payout into the batch.</summary>
    Eligible,

    /// <summary>Excluded (Q3): the order has a partial refund (<c>RefundedAmountMinor &gt; 0</c>).</summary>
    ExcludedPartiallyRefunded,

    /// <summary>Excluded (Q5): the order's maker has no bank account on file.</summary>
    ExcludedNoBankAccount,

    /// <summary>Not a candidate at all (not Delivered, or already batched). Belt-and-braces.</summary>
    NotClaimable,
}

/// <summary>
/// Pure, table-driven eligibility specification per T-0102a §C.12 — the
/// single source of truth for "should this Delivered order be claimed into
/// the weekly payout batch?". No infrastructure, no DB: the repository
/// query supplies candidates; this classifies each one.
///
/// <para>
/// <b>Classification precedence</b> (fixed): NotClaimable →
/// ExcludedPartiallyRefunded (Q3) → ExcludedNoBankAccount (Q5) → Eligible.
/// An order that is both partially refunded AND has no bank account
/// resolves to <see cref="PayoutEligibilityVerdict.ExcludedPartiallyRefunded"/>
/// (the refund is the more actionable signal for the admin).
/// </para>
/// </summary>
public static class PayoutEligibility
{
    public static PayoutEligibilityVerdict Classify(
        OrderState state,
        string? payoutBatchId,
        long refundedAmountMinor,
        string? makerBankAccount)
    {
        // Belt-and-braces — the repository query already filters these out.
        if (state != OrderState.Delivered || !string.IsNullOrWhiteSpace(payoutBatchId))
        {
            return PayoutEligibilityVerdict.NotClaimable;
        }

        // Q3 — partially-refunded orders ride the next batch after admin
        // resolution. Higher precedence than the no-bank exclusion.
        if (refundedAmountMinor > 0)
        {
            return PayoutEligibilityVerdict.ExcludedPartiallyRefunded;
        }

        // Q5 — a maker with no bank account cannot appear in any CSV row.
        if (string.IsNullOrWhiteSpace(makerBankAccount))
        {
            return PayoutEligibilityVerdict.ExcludedNoBankAccount;
        }

        return PayoutEligibilityVerdict.Eligible;
    }
}
