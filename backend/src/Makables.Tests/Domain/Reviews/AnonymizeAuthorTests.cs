using FluentAssertions;
using Makables.Core.Domain.Reviews;

namespace Makables.Tests.Domain.Reviews;

/// <summary>
/// Pure-logic transform: <see cref="Review.AnonymizeAuthor"/> de-identifies
/// the author (the <see cref="Review.CustomerUserId"/> audit-trail id →
/// <c>"Anonymized"</c>) while keeping the review content (rating + body) —
/// the review is about the maker, so it stays in the public corpus. Written
/// red-first per the TDD policy (T-0110, locked Q-A erasure matrix).
/// </summary>
public sealed class AnonymizeAuthorTests
{
    private static Review BuildReview() =>
        Review.Create(
            id: "rev-1",
            orderId: "ord-1",
            makerId: "maker-1",
            customerUserId: "user-1",
            rating: 5,
            body: "Krásný výtisk, rychlé dodání.",
            countryCode: "CZ");

    [Fact]
    public void AnonymizeAuthor_de_identifies_the_author_and_keeps_the_content()
    {
        var review = BuildReview();

        review.AnonymizeAuthor();

        review.CustomerUserId.Should().Be("Anonymized");
        review.Rating.Should().Be((short)5, "the rating is about the maker, it stays");
        review.Body.Should().Be("Krásný výtisk, rychlé dodání.", "the content stays");
    }

    [Fact]
    public void AnonymizeAuthor_is_idempotent()
    {
        var review = BuildReview();

        review.AnonymizeAuthor();
        var act = () => review.AnonymizeAuthor();

        act.Should().NotThrow();
        review.CustomerUserId.Should().Be("Anonymized");
    }
}
