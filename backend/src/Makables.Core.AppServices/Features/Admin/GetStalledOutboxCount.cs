using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin overview count: how many <see cref="OutboxEvent"/> rows are
/// <b>stalled</b> (T-0126 / Q-0027) — the retry ladder exhausted and admin
/// intervention is the only legal next state. The predicate lives in
/// <see cref="IOutboxConsumerRepository.CountStalledAsync"/> and matches
/// T-0109's stalled set exactly
/// (<c>ProcessedAt == null AND NextRetryAt == null AND LastErrorKind != None</c>).
/// Read-only; drives the overview's stalled-outbox KPI tile + the
/// US-admin-0002 AC-2 banner. No params, no failure mode — empty set →
/// <c>{ count: 0 }</c>, never 404.
/// </summary>
public static class GetStalledOutboxCount
{
    public sealed record Query : IQuery<GetStalledOutboxCountResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetStalledOutboxCountResponse(int Count);

    /// <summary>
    /// No-op validator — the query carries no params (the stalled predicate is
    /// fixed). Present to satisfy the one-file feature shape (T1) + the
    /// ValidationPipelineBehavior's per-request contract.
    /// </summary>
    public sealed class Validator : AbstractValidator<Query>
    {
    }

    public sealed class Handler(IOutboxConsumerRepository outbox)
        : IRequestHandler<Query, BusinessResult<GetStalledOutboxCountResponse>>
    {
        public async Task<BusinessResult<GetStalledOutboxCountResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var count = await outbox.CountStalledAsync(cancellationToken);
            return BusinessResult.Success(new GetStalledOutboxCountResponse(count));
        }
    }
}
