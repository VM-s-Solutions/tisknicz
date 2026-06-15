using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin overview count: how many <see cref="PayoutBatch"/> rows are in a given
/// <see cref="PayoutBatchState"/> (T-0126 / Q-0027, default
/// <see cref="PayoutBatchState.Processing"/>). Read-only, admin-host only
/// (the <see cref="IPayoutBatchRepository"/> is the privileged money-record
/// seam per ADR 0013). Replaces the overview's "—" placeholder tile. No audit
/// row (reads are not audited); no failure mode — an empty set returns
/// <c>{ count: 0 }</c>, never 404.
/// </summary>
public static class GetProcessingPayoutsCount
{
    public sealed record Query(PayoutBatchState State) : IQuery<GetProcessingPayoutsCountResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetProcessingPayoutsCountResponse(int Count);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.State)
                .Cascade(CascadeMode.Stop)
                .IsInEnum()
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(IPayoutBatchRepository payoutBatches)
        : IRequestHandler<Query, BusinessResult<GetProcessingPayoutsCountResponse>>
    {
        public async Task<BusinessResult<GetProcessingPayoutsCountResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var count = await payoutBatches.CountByStateAsync(query.State, cancellationToken);
            return BusinessResult.Success(new GetProcessingPayoutsCountResponse(count));
        }
    }
}
