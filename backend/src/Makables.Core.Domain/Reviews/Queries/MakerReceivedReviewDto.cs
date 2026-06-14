namespace Makables.Core.Domain.Reviews.Queries;

/// <summary>
/// One review about the maker, for the maker-dashboard "reviews about me"
/// list (T-0100 §C.7). Carries NO customer identity (no email/name) —
/// GDPR data-minimization consistent with T-0079 / T-0081: the maker
/// sees the review, not who wrote it. Read-side projection DTO (ADR 0023).
/// </summary>
public sealed record MakerReceivedReviewDto(
    string ReviewId,
    string OrderId,
    string OrderNumber,
    short Rating,
    string? Body,
    string? MakerReply,
    DateTimeOffset? MakerReplyAt,
    DateTimeOffset CreatedAt);
