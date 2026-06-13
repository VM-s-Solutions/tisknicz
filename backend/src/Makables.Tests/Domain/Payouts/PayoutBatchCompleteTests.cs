using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;
using NSubstitute;

namespace Makables.Tests.Domain.Payouts;

/// <summary>
/// T-0103 red-first pins for the <see cref="PayoutBatch.Complete"/> domain
/// seam. The batch is born <see cref="PayoutBatchState.Processing"/>
/// (T-0101) and moves forward-only to <see cref="PayoutBatchState.Completed"/>
/// when the operator records the executed wire. A second
/// <see cref="PayoutBatch.Complete"/> on an already-completed batch is
/// refused at the guard (<see cref="BusinessErrorMessage.PayoutBatchNotProcessing"/>)
/// — the handler owns the Silent-Success echo; the domain method is reached
/// only on the live transition. Blank inputs are impossible (Validator +
/// session check upstream) → <see cref="ArgumentException"/>.
/// </summary>
public class PayoutBatchCompleteTests
{
    private static PayoutBatch ProcessingBatch() => PayoutBatch.Create(
        id: "pb-1",
        batchNumber: "VYP-CZ-2026-W24",
        countryCode: "CZ",
        totalAmountMinor: 123456,
        currency: "CZK",
        orderCount: 3,
        makerCount: 2,
        excludedPartiallyRefundedOrderCount: 0,
        excludedNoBankAccountOrderCount: 0,
        excludedNoBankAccountMakerCount: 0);

    private static IClock ClockAt(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }

    [Fact]
    public void Processing_to_Completed_sets_all_fields()
    {
        var now = new DateTimeOffset(2026, 6, 13, 9, 30, 0, TimeSpan.Zero);
        var batch = ProcessingBatch();

        var result = batch.Complete(ClockAt(now), "REF-123", paymentDate: null, completedBy: "admin-1");

        result.IsSuccess.Should().BeTrue();
        batch.State.Should().Be(PayoutBatchState.Completed);
        batch.BankReference.Should().Be("REF-123");
        batch.CompletedBy.Should().Be("admin-1");
        batch.CompletedAt.Should().Be(now);
    }

    [Fact]
    public void PaymentDate_provided_sets_CompletedAt_to_midnight_utc()
    {
        // clock.UtcNow deliberately differs from the supplied value date.
        var now = new DateTimeOffset(2026, 6, 13, 9, 30, 0, TimeSpan.Zero);
        var batch = ProcessingBatch();

        var result = batch.Complete(ClockAt(now), "REF", new DateOnly(2026, 6, 10), "admin");

        result.IsSuccess.Should().BeTrue();
        batch.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void BankReference_is_trimmed()
    {
        var batch = ProcessingBatch();

        batch.Complete(ClockAt(DateTimeOffset.UtcNow), "  REF-9  ", null, "admin");

        batch.BankReference.Should().Be("REF-9");
    }

    [Fact]
    public void Complete_on_already_Completed_batch_returns_NotProcessing_and_keeps_fields()
    {
        var firstNow = new DateTimeOffset(2026, 6, 13, 9, 30, 0, TimeSpan.Zero);
        var batch = ProcessingBatch();
        batch.Complete(ClockAt(firstNow), "REF-FIRST", new DateOnly(2026, 6, 11), "admin-1");

        var second = batch.Complete(
            ClockAt(firstNow.AddDays(1)), "REF-SECOND", new DateOnly(2026, 6, 12), "admin-2");

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(BusinessErrorMessage.PayoutBatchNotProcessing);
        // The first completion is authoritative — unchanged.
        batch.BankReference.Should().Be("REF-FIRST");
        batch.CompletedBy.Should().Be("admin-1");
        batch.CompletedAt.Should().Be(new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_bankReference_throws(string bankReference)
    {
        var batch = ProcessingBatch();
        var act = () => batch.Complete(ClockAt(DateTimeOffset.UtcNow), bankReference, null, "admin");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_completedBy_throws(string completedBy)
    {
        var batch = ProcessingBatch();
        var act = () => batch.Complete(ClockAt(DateTimeOffset.UtcNow), "REF", null, completedBy);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Oversize_bankReference_throws()
    {
        var batch = ProcessingBatch();
        var tooLong = new string('x', PayoutBatch.MaxBankReferenceLength + 1);
        var act = () => batch.Complete(ClockAt(DateTimeOffset.UtcNow), tooLong, null, "admin");
        act.Should().Throw<ArgumentException>();
    }
}
