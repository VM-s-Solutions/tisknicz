using FluentAssertions;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payouts;

namespace Makables.Tests.Domain.Payouts;

/// <summary>
/// T-0102a red-first table for <see cref="PayoutEligibility.Classify"/> —
/// the pure, single-source-of-truth verdict per order. Classification
/// precedence (locked §C.12): NotClaimable → ExcludedPartiallyRefunded
/// (Q3) → ExcludedNoBankAccount (Q5) → Eligible.
/// </summary>
public class PayoutEligibilityTests
{
    [Fact]
    public void Delivered_unbatched_unrefunded_with_bank_is_eligible()
    {
        PayoutEligibility.Classify(
            OrderState.Delivered, payoutBatchId: null, refundedAmountMinor: 0, makerBankAccount: "123/0100")
            .Should().Be(PayoutEligibilityVerdict.Eligible);
    }

    [Theory]
    [InlineData(OrderState.PendingPayment)]
    [InlineData(OrderState.Paid)]
    [InlineData(OrderState.Accepted)]
    [InlineData(OrderState.Shipped)]
    [InlineData(OrderState.Completed)]
    [InlineData(OrderState.Cancelled)]
    [InlineData(OrderState.Refunded)]
    [InlineData(OrderState.Disputed)]
    public void Non_delivered_states_are_not_claimable(OrderState state)
    {
        PayoutEligibility.Classify(state, payoutBatchId: null, refundedAmountMinor: 0, makerBankAccount: "123/0100")
            .Should().Be(PayoutEligibilityVerdict.NotClaimable);
    }

    [Fact]
    public void Already_batched_delivered_order_is_not_claimable()
    {
        PayoutEligibility.Classify(
            OrderState.Delivered, payoutBatchId: "pb-1", refundedAmountMinor: 0, makerBankAccount: "123/0100")
            .Should().Be(PayoutEligibilityVerdict.NotClaimable);
    }

    [Fact]
    public void Partially_refunded_delivered_order_is_excluded_q3()
    {
        PayoutEligibility.Classify(
            OrderState.Delivered, payoutBatchId: null, refundedAmountMinor: 5000, makerBankAccount: "123/0100")
            .Should().Be(PayoutEligibilityVerdict.ExcludedPartiallyRefunded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_bank_account_delivered_order_is_excluded_q5(string? bankAccount)
    {
        PayoutEligibility.Classify(
            OrderState.Delivered, payoutBatchId: null, refundedAmountMinor: 0, makerBankAccount: bankAccount)
            .Should().Be(PayoutEligibilityVerdict.ExcludedNoBankAccount);
    }

    [Fact]
    public void Refunded_and_no_bank_resolves_to_partially_refunded_precedence()
    {
        // Precedence: ExcludedPartiallyRefunded wins over ExcludedNoBankAccount.
        PayoutEligibility.Classify(
            OrderState.Delivered, payoutBatchId: null, refundedAmountMinor: 5000, makerBankAccount: null)
            .Should().Be(PayoutEligibilityVerdict.ExcludedPartiallyRefunded);
    }

    [Fact]
    public void Not_claimable_wins_over_exclusions()
    {
        // A non-Delivered order with a refund and no bank still resolves
        // NotClaimable (precedence head).
        PayoutEligibility.Classify(
            OrderState.Cancelled, payoutBatchId: null, refundedAmountMinor: 5000, makerBankAccount: null)
            .Should().Be(PayoutEligibilityVerdict.NotClaimable);
    }
}
