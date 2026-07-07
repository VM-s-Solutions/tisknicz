using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;

namespace Makables.Tests.Domain.Payouts;

/// <summary>
/// T-0146 pins for <see cref="PayoutDeduction"/> — the maker-borne
/// return-shipping cost negative line item (Q-0037 resolution). Covers
/// factory validation and the set-once <see cref="PayoutDeduction.ApplyToPayoutBatch"/>
/// claim.
/// </summary>
public class PayoutDeductionTests
{
    private static PayoutDeduction ValidDeduction() => PayoutDeduction.Create(
        id: "pd-1",
        makerId: "maker-1",
        disputeId: "disp-1",
        reason: PayoutDeductionReason.ReturnShippingCost,
        amountMinor: 7900,
        currency: "CZK",
        countryCode: "CZ");

    [Fact]
    public void Create_with_valid_inputs_succeeds()
    {
        var deduction = ValidDeduction();

        deduction.MakerId.Should().Be("maker-1");
        deduction.DisputeId.Should().Be("disp-1");
        deduction.Reason.Should().Be(PayoutDeductionReason.ReturnShippingCost);
        deduction.AmountMinor.Should().Be(7900);
        deduction.Currency.Should().Be("CZK");
        deduction.PayoutBatchId.Should().BeNull("born unclaimed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_with_non_positive_amount_throws(long amount)
    {
        var act = () => PayoutDeduction.Create(
            "pd-1", "maker-1", "disp-1", PayoutDeductionReason.ReturnShippingCost,
            amount, "CZK", "CZ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyToPayoutBatch_first_call_sets_the_batch_id()
    {
        var deduction = ValidDeduction();

        var result = deduction.ApplyToPayoutBatch("pb-1");

        result.IsSuccess.Should().BeTrue();
        deduction.PayoutBatchId.Should().Be("pb-1");
    }

    [Fact]
    public void ApplyToPayoutBatch_second_call_is_loud_conflict()
    {
        var deduction = ValidDeduction();
        deduction.ApplyToPayoutBatch("pb-1");

        var result = deduction.ApplyToPayoutBatch("pb-2");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PayoutDeductionAlreadyApplied);
        deduction.PayoutBatchId.Should().Be("pb-1", "a deduction is consumed by exactly one batch");
    }
}
