using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Reviews;
using Makables.Core.Domain.Reviews.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Reviews;

/// <summary>
/// Customer-host read: the customer's Delivered/Completed orders that
/// have no active review yet (T-0100 §C.6). Powers the "leave a review"
/// CTA list. Owner-scoped at the SQL level (IDOR shield). Small set per
/// customer — no paging.
/// </summary>
public static class GetCustomerReviewableOrders
{
    public sealed record Query : IQuery<GetCustomerReviewableOrdersResponse>;

    public sealed record GetCustomerReviewableOrdersResponse(IReadOnlyList<ReviewableOrderDto> Orders);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
        }
    }

    public sealed class Handler(
        IReviewQueries reviews,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetCustomerReviewableOrdersResponse>>
    {
        public async Task<BusinessResult<GetCustomerReviewableOrdersResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<GetCustomerReviewableOrdersResponse>(Error.Unauthorized());
            }

            var orders = await reviews.GetCustomerReviewableOrdersAsync(customerUserId, cancellationToken);
            return BusinessResult.Success(new GetCustomerReviewableOrdersResponse(orders));
        }
    }
}
