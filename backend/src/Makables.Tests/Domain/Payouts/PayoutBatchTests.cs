using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;

namespace Makables.Tests.Domain.Payouts;

/// <summary>
/// T-0101 red-first pins for the <see cref="PayoutBatch"/> aggregate.
/// The batch is born directly in <see cref="PayoutBatchState.Processing"/>
/// (lock A.4 — no observable Pending), is immutable thereafter except for
/// the set-once <see cref="PayoutBatch.AttachCsvBlobPath"/>, and the
/// factory throws <see cref="ArgumentException"/> for every impossible
/// input (an empty batch never reaches this code — the claim handler
/// short-circuits with payoutBatch.empty).
/// </summary>
public class PayoutBatchTests
{
    private static PayoutBatch ValidBatch() => PayoutBatch.Create(
        id: "pb-1",
        batchNumber: "VYP-CZ-2026-W24",
        countryCode: "CZ",
        totalAmountMinor: 123456,
        currency: "CZK",
        orderCount: 3,
        makerCount: 2,
        excludedPartiallyRefundedOrderCount: 1,
        excludedNoBankAccountOrderCount: 1,
        excludedNoBankAccountMakerCount: 1);

    // === AC-1: happy path ===

    [Fact]
    public void Create_happy_path_sets_all_fields_and_is_born_processing()
    {
        var batch = ValidBatch();

        batch.Id.Should().Be("pb-1");
        batch.BatchNumber.Should().Be("VYP-CZ-2026-W24");
        batch.CountryCode.Should().Be("CZ");
        batch.State.Should().Be(PayoutBatchState.Processing);
        batch.TotalAmountMinor.Should().Be(123456);
        batch.Currency.Should().Be("CZK");
        batch.OrderCount.Should().Be(3);
        batch.MakerCount.Should().Be(2);
        batch.ExcludedPartiallyRefundedOrderCount.Should().Be(1);
        batch.ExcludedNoBankAccountOrderCount.Should().Be(1);
        batch.ExcludedNoBankAccountMakerCount.Should().Be(1);
        batch.CsvBlobPath.Should().BeNull();
        batch.CompletedAt.Should().BeNull();
        batch.CompletedBy.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_blank_id(string id)
    {
        var act = () => PayoutBatch.Create(id, "VYP-CZ-2026-W24", "CZ", 1, "CZK", 1, 1, 0, 0, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_blank_batch_number(string batchNumber)
    {
        var act = () => PayoutBatch.Create("pb-1", batchNumber, "CZ", 1, "CZK", 1, 1, 0, 0, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_on_blank_country_code(string countryCode)
    {
        var act = () => PayoutBatch.Create("pb-1", "VYP-CZ-2026-W24", countryCode, 1, "CZK", 1, 1, 0, 0, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("CZK1")]
    [InlineData("CZ")]
    [InlineData("")]
    public void Create_throws_on_invalid_currency_length(string currency)
    {
        var act = () => PayoutBatch.Create("pb-1", "VYP-CZ-2026-W24", "CZ", 1, currency, 1, 1, 0, 0, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 1, 1)]    // total <= 0
    [InlineData(-5, 1, 1)]   // total negative
    [InlineData(100, 0, 1)]  // orderCount < 1
    [InlineData(100, 1, 0)]  // makerCount < 1
    [InlineData(100, 1, 2)]  // makerCount > orderCount
    public void Create_throws_on_nonpositive_amounts_and_counts(long total, int orderCount, int makerCount)
    {
        var act = () => PayoutBatch.Create(
            "pb-1", "VYP-CZ-2026-W24", "CZ", total, "CZK", orderCount, makerCount, 0, 0, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Create_throws_on_negative_exclusion_counts(
        int excludedRefunded, int excludedNoBankOrders, int excludedNoBankMakers)
    {
        var act = () => PayoutBatch.Create(
            "pb-1", "VYP-CZ-2026-W24", "CZ", 100, "CZK", 1, 1,
            excludedRefunded, excludedNoBankOrders, excludedNoBankMakers);
        act.Should().Throw<ArgumentException>();
    }

    // === AC-2: AttachCsvBlobPath set-once ===

    [Fact]
    public void AttachCsvBlobPath_sets_once()
    {
        var batch = ValidBatch();

        var result = batch.AttachCsvBlobPath("payouts/cz/VYP-CZ-2026-W24.csv");

        result.IsSuccess.Should().BeTrue();
        batch.CsvBlobPath.Should().Be("payouts/cz/VYP-CZ-2026-W24.csv");
    }

    [Fact]
    public void AttachCsvBlobPath_idempotent_same_value_succeeds_different_value_fails()
    {
        var batch = ValidBatch();
        batch.AttachCsvBlobPath("payouts/cz/VYP-CZ-2026-W24.csv");

        // Same value → idempotent success.
        batch.AttachCsvBlobPath("payouts/cz/VYP-CZ-2026-W24.csv").IsSuccess.Should().BeTrue();

        // Different value → failure with the set-once code.
        var conflict = batch.AttachCsvBlobPath("payouts/cz/OTHER.csv");
        conflict.IsSuccess.Should().BeFalse();
        conflict.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchCsvPathAlreadySet);
    }
}
