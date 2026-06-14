using FluentAssertions;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Reviews;

namespace Makables.Tests.Domain.Makers;

/// <summary>
/// T-0100 §C.4 pure-logic pins for the recompute producer that wires the
/// previously-dormant T-0043 <see cref="Maker.RatingAverageBp"/> /
/// <see cref="Maker.RatingCount"/> fields, plus the §C.8-step-3
/// eligibility predicate (<see cref="ReviewEligibility.IsReviewableState"/>).
/// </summary>
public class MakerRecomputeRatingTests
{
    private static Maker BuildMaker()
    {
        var maker = Maker.Create(
            id: "maker-1", userId: "user-maker-1",
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: DateTimeOffset.UnixEpoch, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast");
        // Seed a non-zero TotalOrders to prove the recompute leaves it alone.
        maker.SetCatalogStats(0, 0, 7);
        return maker;
    }

    [Fact]
    public void RecomputeRating_stores_bp_and_count_via_SetCatalogStats()
    {
        var maker = BuildMaker();

        maker.RecomputeRating(ratingCount: 3, ratingAverageBp: 40_000);

        maker.RatingCount.Should().Be(3);
        maker.RatingAverageBp.Should().Be(40_000);
    }

    [Fact]
    public void RecomputeRating_leaves_TotalOrders_untouched()
    {
        var maker = BuildMaker();

        maker.RecomputeRating(ratingCount: 1, ratingAverageBp: 50_000);

        maker.TotalOrders.Should().Be(7, "the order-completion flow owns TotalOrders, not the recompute");
    }

    [Fact]
    public void RecomputeRating_out_of_range_bp_throws_via_delegated_guard()
    {
        var maker = BuildMaker();

        var act = () => maker.RecomputeRating(ratingCount: 1, ratingAverageBp: 50_001);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(OrderState.Delivered, true)]
    [InlineData(OrderState.Completed, true)]
    [InlineData(OrderState.PendingPayment, false)]
    [InlineData(OrderState.Paid, false)]
    [InlineData(OrderState.Accepted, false)]
    [InlineData(OrderState.Shipped, false)]
    [InlineData(OrderState.Cancelled, false)]
    [InlineData(OrderState.Refunded, false)]
    [InlineData(OrderState.Disputed, false)]
    public void IsReviewableState_accepts_delivered_and_completed_only(OrderState state, bool expected)
    {
        ReviewEligibility.IsReviewableState(state).Should().Be(expected);
    }
}
