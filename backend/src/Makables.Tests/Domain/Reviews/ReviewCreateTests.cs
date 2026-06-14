using FluentAssertions;
using Makables.Core.Domain.Reviews;

namespace Makables.Tests.Domain.Reviews;

/// <summary>
/// T-0100 §C.2 pure-logic pins for <see cref="Review.Create"/>: the
/// rating range guard (1..5), the body length cap (1000), and the
/// happy-path trim + null-body semantics. The rating/length paths ALSO
/// run in the SubmitReview Validator so the typed
/// <c>ReviewRatingOutOfRange</c> / <c>ReviewBodyTooLong</c> surface
/// before the aggregate; these tests pin the defensive domain tail.
/// </summary>
public class ReviewCreateTests
{
    private const string Id = "rev-1";
    private const string OrderId = "ord-1";
    private const string MakerId = "maker-1";
    private const string CustomerUserId = "user-1";
    private const string CountryCode = "CZ";

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]
    [InlineData((short)6)]
    public void Create_rating_outside_1_to_5_throws(short rating)
    {
        var act = () => Review.Create(
            Id, OrderId, MakerId, CustomerUserId, rating, "Skvělé.", CountryCode);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_body_over_max_length_throws()
    {
        var tooLong = new string('x', Review.MaxBodyLength + 1);

        var act = () => Review.Create(
            Id, OrderId, MakerId, CustomerUserId, 5, tooLong, CountryCode);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_happy_path_trims_body_leaves_reply_null_and_stamps_fields()
    {
        var review = Review.Create(
            Id, OrderId, MakerId, CustomerUserId, 4, "  Pěkná práce.  ", CountryCode);

        review.Rating.Should().Be(4);
        review.Body.Should().Be("Pěkná práce.", "the factory trims the body");
        review.OrderId.Should().Be(OrderId);
        review.MakerId.Should().Be(MakerId);
        review.CustomerUserId.Should().Be(CustomerUserId);
        review.MakerReply.Should().BeNull();
        review.MakerReplyAt.Should().BeNull();
        review.CountryCode.Should().Be("CZ");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_accepts_null_or_whitespace_body_as_null(string? body)
    {
        var review = Review.Create(
            Id, OrderId, MakerId, CustomerUserId, 3, body, CountryCode);

        review.Body.Should().BeNull("null / whitespace body collapses to null");
    }
}
