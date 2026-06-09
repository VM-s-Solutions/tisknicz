using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Orders.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Maker-facing order detail (T-0082 / US-maker-0010). Lifecycle
/// timestamps + price breakdown including the maker's net payout +
/// customer contact NAME + PHONE (never email — T-0081 §A.2 GDPR lock)
/// + carrier ref + Zasilkovna pickup point + inline attachments +
/// invoice PDF URL.
///
/// <para>
/// <b>IDOR shield enforced TWICE.</b> (1) Handler resolves the
/// requesting maker via <see cref="IMakerRepository.GetByUserIdAsync"/>
/// from the session-bound user. (2) EF query then filters on
/// <c>o.MakerId == maker.Id</c>. Either layer alone would suffice; the
/// two-layer setup means a future admin tool cannot accidentally bypass
/// (2).
/// </para>
/// </summary>
public static class GetMakerOrderDetails
{
    public sealed record Query(string OrderId) : IQuery<GetMakerOrderDetailsResponse>;

    /// <summary>
    /// Globally-unique wrapper name to avoid NSwag TS class collision.
    /// </summary>
    public sealed record GetMakerOrderDetailsResponse(MakerOrderDetailDto Detail);

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
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerOrderDetailsResponse>>
    {
        public async Task<BusinessResult<GetMakerOrderDetailsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerOrderDetailsResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetMakerOrderDetailsResponse>(
                    Error.NotFound("maker", BusinessErrorMessage.MakerNotFound));
            }

            var dto = await orderQueries.GetMakerOrderDetailsAsync(
                query.OrderId, maker.Id, cancellationToken);
            if (dto is null)
            {
                return BusinessResult.Failure<GetMakerOrderDetailsResponse>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            return BusinessResult.Success(new GetMakerOrderDetailsResponse(dto));
        }
    }
}
