using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Reviews;
using Makables.Core.Domain.Reviews.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Reviews;

/// <summary>
/// Customer-host read: the reviews the customer themselves submitted
/// (T-0100 §C.6) — rating + body + the maker's reply if any. Powers the
/// customer-dashboard read-back + the T-0115 order-page read-only block.
/// Owner-scoped at the SQL level. Small set — no paging.
/// </summary>
public static class GetCustomerSubmittedReviews
{
    public sealed record Query : IQuery<GetCustomerSubmittedReviewsResponse>;

    public sealed record GetCustomerSubmittedReviewsResponse(IReadOnlyList<SubmittedReviewDto> Reviews);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
        }
    }

    public sealed class Handler(
        IReviewQueries reviews,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetCustomerSubmittedReviewsResponse>>
    {
        public async Task<BusinessResult<GetCustomerSubmittedReviewsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<GetCustomerSubmittedReviewsResponse>(Error.Unauthorized());
            }

            var submitted = await reviews.GetCustomerSubmittedReviewsAsync(customerUserId, cancellationToken);
            return BusinessResult.Success(new GetCustomerSubmittedReviewsResponse(submitted));
        }
    }
}
