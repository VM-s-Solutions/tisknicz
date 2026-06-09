using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using Makables.Core.Domain.Orders.Sorting;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Customer dashboard order list (T-0080). Owner-scoped paged read via
/// the new <see cref="IOrderQueries"/> seam (ADR 0023 — read-side split
/// from the write-scoped <see cref="IOrderRepository"/>).
///
/// <para>
/// <b>IDOR shield.</b> The owning customer id is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/>; the query then filters
/// on <c>o.CustomerUserId == customerId</c> baked into the SQL <c>WHERE</c>
/// clause. There is no <c>CustomerId</c> field in the query shape — a
/// cross-tenant probe simply returns an empty page with
/// <c>TotalCount = 0</c>, not 404 (AC-5).
/// </para>
///
/// <para>
/// Page size is clamped to <see cref="MaxPageSize"/> via the validator
/// (matches T-0049a's <c>GetMyProducts</c> precedent); larger pages would
/// only blow the projection without UX benefit. Sort defaults to
/// <see cref="OrderSort.CreatedAtDesc"/> at the controller binding.
/// </para>
/// </summary>
public static class GetCustomerOrders
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        OrderState? State,
        DateTimeOffset? DateFrom,
        DateTimeOffset? DateTo,
        OrderSort Sort) : IQuery<GetCustomerOrdersResponse>;

    /// <summary>
    /// Wrapper around the <see cref="PagedData{T}"/> envelope.
    /// <b>Globally-unique name</b> (not nested <c>Response</c>) to avoid
    /// the NSwag TS class collision documented at the shipping bundle CI
    /// fix. T-0080 §C "Globally-unique Response naming".
    /// </summary>
    public sealed record GetCustomerOrdersResponse(PagedData<CustomerOrderListItemDto> Orders);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            // Upper-bound Page so (Page-1)*PageSize can't overflow Int32
            // into a negative Skip offset (T-0043 / T-0049a precedent).
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

            // Inverted date range — surfaces as a single validation error
            // on DateFrom so the message hangs off the field the caller
            // can adjust. Generic MinValue code keeps i18n free
            // (BusinessErrorMessage.MinValue maps to validation.minValue,
            // which is the closest "range bound violated" key already
            // shipped per CLAUDE.md "no new BusinessErrorMessage codes" lock).
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
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetCustomerOrdersResponse>>
    {
        public async Task<BusinessResult<GetCustomerOrdersResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<GetCustomerOrdersResponse>(Error.Unauthorized());
            }

            var filter = new OrderFilter(query.State, query.DateFrom, query.DateTo);
            var page = await orderQueries.GetCustomerOrdersPagedAsync(
                customerUserId, filter, query.Sort, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetCustomerOrdersResponse(page));
        }
    }
}
