using FluentAssertions;
using Makables.Core.Domain.Reviews;

namespace Makables.Tests.Domain.Reviews;

/// <summary>
/// T-0100 §C.3 pure-logic pins for <see cref="Review.AddReply"/>: the
/// reply length cap (500), the OVERWRITE-not-append semantics (one reply
/// per review, Q4), the <see cref="Review.MakerReplyAt"/> bump, and the
/// immutability of the customer's <see cref="Review.Rating"/> /
/// <see cref="Review.Body"/> under a reply (Q4 asymmetry).
/// </summary>
public class ReviewAddReplyTests
{
    private static readonly DateTimeOffset First = DateTimeOffset.Parse("2026-06-14T10:00:00Z");
    private static readonly DateTimeOffset Second = DateTimeOffset.Parse("2026-06-14T12:30:00Z");

    private static Review BuildReview() =>
        Review.Create("rev-1", "ord-1", "maker-1", "user-1", 5, "Perfektní.", "CZ");

    [Fact]
    public void AddReply_over_max_length_throws()
    {
        var review = BuildReview();
        var tooLong = new string('y', Review.MaxReplyLength + 1);

        var act = () => review.AddReply(tooLong, First);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddReply_empty_or_whitespace_throws(string reply)
    {
        var review = BuildReview();

        var act = () => review.AddReply(reply, First);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Second_AddReply_overwrites_the_first_and_bumps_timestamp()
    {
        var review = BuildReview();
        review.AddReply("  Děkujeme!  ", First);

        review.MakerReply.Should().Be("Děkujeme!", "the reply is trimmed");
        review.MakerReplyAt.Should().Be(First);

        review.AddReply("Děkujeme za zpětnou vazbu, opravíme to.", Second);

        review.MakerReply.Should().Be("Děkujeme za zpětnou vazbu, opravíme to.",
            "a second reply OVERWRITES the first — one reply per review (Q4)");
        review.MakerReplyAt.Should().Be(Second, "MakerReplyAt bumps on overwrite");
    }

    [Fact]
    public void AddReply_does_not_mutate_customer_rating_or_body()
    {
        var review = BuildReview();

        review.AddReply("Reakce výrobce.", First);

        review.Rating.Should().Be(5, "the customer review is immutable under a reply (Q4)");
        review.Body.Should().Be("Perfektní.", "the customer body is never touched by AddReply");
    }
}
