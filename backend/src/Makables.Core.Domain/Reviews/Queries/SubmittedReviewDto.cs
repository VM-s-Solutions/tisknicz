namespace Makables.Core.Domain.Reviews.Queries;

/// <summary>
/// One review the customer themselves submitted (T-0100 §C.7) — rating +
/// body + the maker's reply if any. Powers the customer-dashboard "my
/// submitted reviews" read-back + the T-0115 order-page read-only block.
/// Read-side projection DTO (ADR 0023).
/// </summary>
public sealed record SubmittedReviewDto(
    string ReviewId,
    string OrderId,
    string MakerId,
    string MakerCompanyName,
    short Rating,
    string? Body,
    string? MakerReply,
    DateTimeOffset CreatedAt);
