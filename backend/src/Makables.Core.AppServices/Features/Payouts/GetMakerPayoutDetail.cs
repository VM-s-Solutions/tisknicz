using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Maker payout-batch detail with the per-order breakdown (T-0112 /
/// US-maker-0012 AC-3). Owner-scoped via <see cref="IPayoutQueries"/>; a
/// cross-maker OR unknown batch id returns the SAME 404 (no enumeration
/// oracle). IDOR shield enforced twice (handler maker resolution + projection
/// predicate).
/// </summary>
public static class GetMakerPayoutDetail
{
    public sealed record Query(string BatchId) : IQuery<GetMakerPayoutDetailResponse>;

    /// <summary>Globally-unique wrapper name to avoid the NSwag TS class collision.</summary>
    public sealed record GetMakerPayoutDetailResponse(MakerPayoutDetailDto Detail);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.BatchId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IPayoutQueries payoutQueries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerPayoutDetailResponse>>
    {
        public async Task<BusinessResult<GetMakerPayoutDetailResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerPayoutDetailResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetMakerPayoutDetailResponse>(
                    Error.NotFound("maker", BusinessErrorMessage.MakerNotFound));
            }

            var detail = await payoutQueries.GetMakerPayoutDetailAsync(
                maker.Id, query.BatchId, cancellationToken);
            if (detail is null)
            {
                // Unknown OR cross-maker batch id — same 404 shape (no oracle).
                return BusinessResult.Failure<GetMakerPayoutDetailResponse>(
                    Error.NotFound("batchId", BusinessErrorMessage.PayoutBatchNotFound));
            }

            return BusinessResult.Success(new GetMakerPayoutDetailResponse(detail));
        }
    }
}
