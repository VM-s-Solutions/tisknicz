using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Admin;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Admin;

/// <summary>
/// Single privileged admin order header (T-0127 / Q-0024 option a /
/// US-admin-0009). <c>GET /api/v1/admin-orders/{orderId}</c> returns the
/// FULL header — number, state, the complete amount breakdown, country,
/// maker (id + name), customer email + contact snapshot, every lifecycle
/// timestamp — composed over <c>IAdminQueries.GetOrderDetailAsync</c>
/// (<c>Unscoped()</c> + <c>IgnoreQueryFilters()</c> + <c>AsNoTracking</c>),
/// projected to a globally-unique <see cref="AdminOrderDetailDto"/>. Admin
/// is privileged → NO GDPR redaction. Replaces T-0118b's list-row-scan
/// header. Unknown / inactive id → 404 <c>order.notFound</c> (the existing
/// code, reused). No audit row (ADR 0014 audits writes). <c>[Authorize]</c>
/// admin audience (ADR 0013).
/// </summary>
public static class GetAdminOrderDetail
{
    public sealed record Query(string OrderId) : IQuery<GetAdminOrderDetailResponse>;

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record GetAdminOrderDetailResponse(AdminOrderDetailDto Order);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required);
        }
    }

    public sealed class Handler(IAdminQueries adminQueries)
        : IRequestHandler<Query, BusinessResult<GetAdminOrderDetailResponse>>
    {
        public async Task<BusinessResult<GetAdminOrderDetailResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var detail = await adminQueries.GetOrderDetailAsync(query.OrderId, cancellationToken);
            if (detail is null)
            {
                return BusinessResult.Failure<GetAdminOrderDetailResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            return BusinessResult.Success(new GetAdminOrderDetailResponse(detail));
        }
    }
}
