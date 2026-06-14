using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Maker-scoped, read-only, payload-free outbox audit trail for one of the
/// maker's orders (T-0112 / US-maker-0017). Event type + derived
/// <see cref="OutboxDeliveryStatus"/> + timestamp only. A cross-maker / unknown
/// order id returns an EMPTY page (IDOR shield in the projection — not an
/// oracle, not 403). No maker retry (admin-only, AC-2).
/// </summary>
public static class GetMakerOutboxEventsForOrder
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(string OrderId, int Page = 1, int PageSize = DefaultPageSize)
        : IQuery<GetMakerOutboxEventsResponse>;

    /// <summary>Globally-unique wrapper name to avoid the NSwag TS class collision.</summary>
    public sealed record GetMakerOutboxEventsResponse(PagedData<MakerOutboxEventDto> Events);

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
        IPayoutQueries payoutQueries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerOutboxEventsResponse>>
    {
        public async Task<BusinessResult<GetMakerOutboxEventsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerOutboxEventsResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetMakerOutboxEventsResponse>(
                    Error.NotFound("maker", BusinessErrorMessage.MakerNotFound));
            }

            var paged = await payoutQueries.GetMakerOutboxEventsForOrderAsync(
                maker.Id, query.OrderId, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetMakerOutboxEventsResponse(paged));
        }
    }
}
