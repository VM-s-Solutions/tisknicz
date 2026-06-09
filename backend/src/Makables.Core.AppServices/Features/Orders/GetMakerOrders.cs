using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Maker dashboard order list (T-0081 / US-maker-0005). Owner-scoped
/// paged read via the <see cref="IOrderQueries"/> seam — extends the
/// interface T-0080 introduced with a maker-variant projection that
/// surfaces the maker's net payout (<see cref="MakerOrderListItemDto.MakerPayoutAmountMinor"/>)
/// and the customer's contact NAME (not email).
///
/// <para>
/// <b>IDOR shield — enforced TWICE.</b> (1) The handler resolves the
/// requesting maker via <see cref="IMakerRepository.GetByUserIdAsync"/>
/// from the session-bound user; a user with no maker row gets
/// <see cref="BusinessErrorMessage.MakerNotFound"/>. (2) The EF
/// projection then filters on <c>o.MakerId == maker.Id</c>. Either layer
/// alone would suffice; the two-layer setup means a future admin tool
/// that legitimately bypasses (1) cannot accidentally bypass (2).
/// </para>
///
/// <para>
/// <b>Customer email is NEVER on the wire</b> per T-0081 §A.2 GDPR
/// data-minimization lock. Maker-customer communication routes through
/// the T-0079 message thread; direct email exchange is not the channel.
/// </para>
/// </summary>
public static class GetMakerOrders
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        OrderState? State,
        DateTimeOffset? DateFrom,
        DateTimeOffset? DateTo,
        OrderSort Sort) : IQuery<GetMakerOrdersResponse>;

    /// <summary>
    /// Globally-unique wrapper name to avoid NSwag TS class collision.
    /// </summary>
    public sealed record GetMakerOrdersResponse(PagedData<MakerOrderListItemDto> Orders);

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

            RuleFor(q => q.Sort)
                .IsInEnum()
                .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            When(q => q.State.HasValue, () =>
            {
                RuleFor(q => q.State!.Value)
                    .IsInEnum()
                    .WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
            });

            When(q => q.DateFrom.HasValue && q.DateTo.HasValue, () =>
            {
                RuleFor(q => q.DateFrom!.Value)
                    .LessThanOrEqualTo(q => q.DateTo!.Value)
                    .WithErrorCode(BusinessErrorMessage.MinValue);
            });
        }
    }

    public sealed class Handler(
        IOrderQueries orderQueries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerOrdersResponse>>
    {
        public async Task<BusinessResult<GetMakerOrdersResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerOrdersResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetMakerOrdersResponse>(
                    Error.NotFound("maker", BusinessErrorMessage.MakerNotFound));
            }

            var filter = new OrderFilter(query.State, query.DateFrom, query.DateTo);
            var page = await orderQueries.GetMakerOrdersPagedAsync(
                maker.Id, filter, query.Sort, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetMakerOrdersResponse(page));
        }
    }
}
