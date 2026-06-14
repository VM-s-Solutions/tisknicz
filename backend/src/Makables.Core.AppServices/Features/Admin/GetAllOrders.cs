using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Admin cross-tenant order list (T-0111 / US-admin-0009). Read-only,
/// <c>Unscoped()</c> + <c>.IgnoreQueryFilters()</c> (soft-deleted /
/// anonymised rows surface for reconciliation). The privileged admin row
/// carries <see cref="AdminOrderListItemDto.CustomerEmail"/> +
/// <see cref="AdminOrderListItemDto.MakerName"/> (no GDPR redaction —
/// admin is privileged). Filters: state, country, makerId, customerEmail
/// (Q-E, no more). No audit row (reads are not audited, ADR 0014).
/// </summary>
public static class GetAllOrders
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(
        int Page,
        int PageSize,
        OrderState? State,
        string? CountryCode,
        string? MakerId,
        string? CustomerEmail) : IQuery<GetAllOrdersResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetAllOrdersResponse(PagedData<AdminOrderListItemDto> Orders);

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

            When(q => q.State.HasValue, () =>
                RuleFor(q => q.State!.Value)
                    .IsInEnum()
                    .WithErrorCode(BusinessErrorMessage.InvalidEnumValue));

            When(q => !string.IsNullOrEmpty(q.CountryCode), () =>
                RuleFor(q => q.CountryCode!)
                    .Length(2)
                    .WithErrorCode(BusinessErrorMessage.MaxLength));
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAllOrdersResponse>>
    {
        public async Task<BusinessResult<GetAllOrdersResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var filter = new AdminOrderFilter(
                query.State, query.CountryCode, query.MakerId, query.CustomerEmail);

            var page = await adminQueries.GetAllOrdersPagedAsync(
                filter, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetAllOrdersResponse(page));
        }
    }
}
