using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Customer-facing order detail (T-0082 / US-customer-0012). Lifecycle
/// timestamps + price breakdown + maker label + inline attachments +
/// invoice PDF URL.
///
/// <para>
/// <b>IDOR shield.</b> The owning customer id is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/>; the EF query then
/// filters on <c>o.CustomerUserId == customerUserId</c>. Cross-customer
/// probes return <see cref="BusinessErrorMessage.OrderNotFound"/> —
/// same shape as "doesn't exist", so order ids aren't enumerable across
/// customers.
/// </para>
///
/// <para>
/// Customer never sees maker-internal fields (e.g. <c>MakerPayoutAmountMinor</c>)
/// — the DTO type simply does not declare them. Compile-time shield.
/// </para>
/// </summary>
public static class GetCustomerOrderDetails
{
    public sealed record Query(string OrderId) : IQuery<GetCustomerOrderDetailsResponse>;

    /// <summary>
    /// Globally-unique wrapper name to avoid NSwag TS class collision.
    /// </summary>
    public sealed record GetCustomerOrderDetailsResponse(CustomerOrderDetailDto Detail);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderQueries orderQueries,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetCustomerOrderDetailsResponse>>
    {
        public async Task<BusinessResult<GetCustomerOrderDetailsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<GetCustomerOrderDetailsResponse>(Error.Unauthorized());
            }

            var dto = await orderQueries.GetCustomerOrderDetailsAsync(
                query.OrderId, customerUserId, cancellationToken);
            if (dto is null)
            {
                return BusinessResult.Failure<GetCustomerOrderDetailsResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            return BusinessResult.Success(new GetCustomerOrderDetailsResponse(dto));
        }
    }
}
