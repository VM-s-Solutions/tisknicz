namespace Makables.Core.Domain.Reviews.Queries;

/// <summary>
/// One Delivered/Completed order the customer owns that has NO active
/// review yet (T-0100 §C.7). Powers the customer-dashboard "leave a
/// review" CTA list. Read-side projection DTO (ADR 0023). Lives in
/// <c>Core.Domain</c> alongside <see cref="IReviewQueries"/> per the
/// <c>IOrderQueries</c> read-DTO precedent (the read interface returns
/// it, and <c>Core.Domain</c> cannot reference <c>Core.AppServices</c>).
/// </summary>
public sealed record ReviewableOrderDto(
    string OrderId,
    string OrderNumber,
    string MakerId,
    string MakerCompanyName,
    DateTimeOffset DeliveredAt);
