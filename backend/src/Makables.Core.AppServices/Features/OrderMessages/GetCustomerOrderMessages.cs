using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using MediatR;

namespace Makables.Core.AppServices.Features.OrderMessages;

/// <summary>
/// Customer-host paged read of the order-message thread (T-0079
/// US-customer-0014). Owner-scoped projection via the new
/// <see cref="IOrderMessageQueries"/> seam — the predicate IS the IDOR
/// shield.
///
/// <para>
/// Cross-tenant probes (a customer querying another customer's order id)
/// return an empty page with <c>TotalCount = 0</c>, NOT 404, matching
/// the T-0080 list-empty contract — the IDOR shield is a WHERE-predicate
/// at the SQL layer, not a typed not-found.
/// </para>
/// </summary>
public static class GetCustomerOrderMessages
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 50;

    public sealed record Query(string OrderId, int Page, int PageSize)
        : IQuery<GetCustomerOrderMessagesResponse>;

    public sealed record GetCustomerOrderMessagesResponse(PagedData<OrderMessageDto> Messages);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

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

    public sealed class Handler(
        IOrderMessageQueries messageQueries,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetCustomerOrderMessagesResponse>>
    {
        public async Task<BusinessResult<GetCustomerOrderMessagesResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<GetCustomerOrderMessagesResponse>(Error.Unauthorized());
            }

            var page = await messageQueries.GetByOrderForCustomerAsync(
                query.OrderId, customerUserId, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetCustomerOrderMessagesResponse(page));
        }
    }
}
