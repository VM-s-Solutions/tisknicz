using FluentAssertions;
using Makables.Core.AppServices.Features.Reviews;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Reviews;
using NSubstitute;
using MakerEntity = Makables.Core.Domain.Makers.Maker;

namespace Makables.Tests.AppServices.Features.Reviews;

/// <summary>
/// T-0100 <see cref="RespondToReview"/> contract: the happy-path reply
/// set, the maker-scoped IDOR shield (404 <c>ReviewNotFound</c>, no
/// existence leak), and the overwrite path (one reply per review, Q4).
/// </summary>
public class RespondToReviewHandlerTests
{
    private const string ReviewId = "rev-1";
    private const string MakerId = "maker-1";
    private const string MakerUserId = "user-maker-1";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");

    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IMakerRepository _makers = Substitute.For<IMakerRepository>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly RespondToReview.Handler _sut;

    public RespondToReviewHandlerTests()
    {
        _session.GetUserId().Returns(MakerUserId);
        _clock.UtcNow.Returns(Now);
        _makers.GetByUserIdAsync(MakerUserId, Arg.Any<CancellationToken>()).Returns(BuildMaker());
        _sut = new RespondToReview.Handler(_reviews, _makers, _session, _clock);
    }

    private static MakerEntity BuildMaker() =>
        MakerEntity.Create(
            id: MakerId, userId: MakerUserId,
            registrationNumber: "27074358", vatId: null,
            companyName: "Avast s.r.o.", legalForm: null,
            registeredAddressId: "addr-1", incorporatedOn: null,
            isActiveInRegistry: true, sourceRegistry: "ares",
            snapshotFetchedAt: Now, snapshotIsStale: false,
            countryCode: "CZ", slug: "avast");

    private static Review BuildReview() =>
        Review.Create(ReviewId, "ord-1", MakerId, "user-cust-1", 5, "Skvělé.", "CZ");

    [Fact]
    public async Task Happy_path_sets_maker_reply()
    {
        var review = BuildReview();
        _reviews.GetByIdForMakerAsync(ReviewId, MakerId, Arg.Any<CancellationToken>()).Returns(review);

        var result = await _sut.Handle(
            new RespondToReview.Command(ReviewId, "  Děkujeme!  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MakerReply.Should().Be("Děkujeme!", "the reply is trimmed");
        result.Value.MakerReplyAt.Should().Be(Now);
        review.MakerReply.Should().Be("Děkujeme!");
    }

    [Fact]
    public async Task Cross_tenant_review_returns_notFound()
    {
        // The maker-scoped repo returns null for a review on another
        // maker's order — same shape as unknown id (IDOR shield).
        _reviews.GetByIdForMakerAsync(ReviewId, MakerId, Arg.Any<CancellationToken>())
            .Returns((Review?)null);

        var result = await _sut.Handle(
            new RespondToReview.Command(ReviewId, "Odpověď."), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ReviewNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Second_reply_overwrites_the_first()
    {
        var review = BuildReview();
        review.AddReply("První pokus.", Now.AddHours(-1));
        _reviews.GetByIdForMakerAsync(ReviewId, MakerId, Arg.Any<CancellationToken>()).Returns(review);

        var result = await _sut.Handle(
            new RespondToReview.Command(ReviewId, "Opravená odpověď."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        review.MakerReply.Should().Be("Opravená odpověď.", "the reply OVERWRITES the prior one (Q4)");
        review.MakerReplyAt.Should().Be(Now, "MakerReplyAt bumps on overwrite");
        review.Rating.Should().Be(5, "the customer review is untouched");
    }

    [Fact]
    public void Validator_rejects_empty_and_oversize_reply_accepts_valid()
    {
        var validator = new RespondToReview.Validator();

        validator.Validate(new RespondToReview.Command(ReviewId, ""))
            .Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.ReviewReplyEmpty);
        validator.Validate(new RespondToReview.Command(ReviewId, new string('y', Review.MaxReplyLength + 1)))
            .Errors.Should().Contain(e => e.ErrorCode == BusinessErrorMessage.ReviewReplyTooLong);
        validator.Validate(new RespondToReview.Command(ReviewId, "Děkujeme za recenzi.")).IsValid.Should().BeTrue();
    }
}
