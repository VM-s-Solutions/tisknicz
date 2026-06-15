using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Paged <b>stalled</b>-outbox triage list (T-0127 / Q-0029 /
/// US-admin-0014). <c>GET /api/v1/outbox-events/stalled</c> → a
/// <c>PagedData</c> of <see cref="StalledOutboxEventDto"/> (id, eventType,
/// aggregateId, lastErrorCode, retryCount, createdAt) using the EXACT
/// stalled predicate <see cref="IOutboxConsumerRepository.StalledPredicate"/>
/// shares with <see cref="GetStalledOutboxCount"/>'s
/// <c>CountStalledAsync</c> — the triage page browses + retries by VISIBLE
/// id instead of the count + blind by-id surface, and the list can never
/// disagree with the KPI tile. Read-only; no audit row (ADR 0014).
/// <c>[Authorize]</c> admin audience (ADR 0013). Empty set →
/// <c>PagedData</c> with <c>TotalCount = 0</c>, never 404.
/// </summary>
public static class GetStalledOutboxEvents
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(int Page, int PageSize) : IQuery<GetStalledOutboxEventsResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetStalledOutboxEventsResponse(PagedData<StalledOutboxEventDto> Events);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Page)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .Cascade(CascadeMode.Stop)
                .InclusiveBetween(1, MaxPageSize)
                .WithErrorCode(BusinessErrorMessage.MinValue);
        }
    }

    public sealed class Handler(IOutboxConsumerRepository outbox)
        : IRequestHandler<Query, BusinessResult<GetStalledOutboxEventsResponse>>
    {
        public async Task<BusinessResult<GetStalledOutboxEventsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var page = await outbox.GetStalledPagedAsync(
                query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetStalledOutboxEventsResponse(page));
        }
    }
}
