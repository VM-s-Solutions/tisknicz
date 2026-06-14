using FluentAssertions;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Reviews;
using Makables.Core.Domain.Reviews.Queries;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.AppServices.Features.Reviews;

/// <summary>
/// T-0100 / T-0117 <see cref="GetMakerReceivedReviews"/> contract: the
/// response surfaces the maker's authoritative aggregate
/// (<c>RatingAverageBp</c> / <c>RatingCount</c>) for the dashboard header
/// (§A.3, NOT averaged from the page), and an unknown-maker session
/// returns an empty page with a zeroed aggregate (no leak).
/// </summary>
public class GetMakerReceivedReviewsHandlerTests
{
    private const string MakerUserId = "user-maker-1";
    private const string MakerId = "maker-1";

    private readonly IReviewQueries _reviews = Substitute.For<IReviewQueries>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetMakerReceivedReviews.Handler _sut;

    public GetMakerReceivedReviewsHandlerTests()
    {
        _session.GetUserId().Returns(MakerUserId);
        _sut = new GetMakerReceivedReviews.Handler(_reviews, _makers, _session);
    }

    private static MakerEntity BuildMaker(int ratingCount, int ratingAverageBp)
    {
        var m = MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: DateTimeOffset.UnixEpoch, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast");
        m.SetCatalogStats(ratingAverageBp, ratingCount, 0);
        return m;
    }

    [Fact]
    public async Task Surfaces_maker_aggregate_for_the_dashboard_header()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>())
            .Returns(BuildMaker(ratingCount: 12, ratingAverageBp: 43_750));
        _reviews.GetMakerReceivedReviewsPagedAsync(MakerId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(PagedData<MakerReceivedReviewDto>.Empty(1, 20));

        var result = await _sut.Handle(new GetMakerReceivedReviews.Query(1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RatingCount.Should().Be(12, "the header reads the maker's authoritative count");
        result.Value.RatingAverageBp.Should().Be(43_750, "the header reads RatingAverageBp, not a page average");
    }

    [Fact]
    public async Task Unknown_maker_returns_empty_page_with_zeroed_aggregate()
    {
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns((MakerEntity?)null);

        var result = await _sut.Handle(new GetMakerReceivedReviews.Query(1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Reviews.TotalCount.Should().Be(0);
        result.Value.RatingCount.Should().Be(0);
        result.Value.RatingAverageBp.Should().Be(0);
    }
}
