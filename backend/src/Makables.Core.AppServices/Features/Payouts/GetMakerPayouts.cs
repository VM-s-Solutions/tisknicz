using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Payouts.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Maker dashboard payout-batch list (T-0112 / US-maker-0012). Owner-scoped
/// paged read via the <see cref="IPayoutQueries"/> seam. Each row carries the
/// per-maker slice (SUM of THIS maker's payout in the batch), never the
/// cross-maker batch total.
///
/// <para>
/// <b>IDOR shield — enforced TWICE.</b> (1) The handler resolves the
/// requesting maker via <see cref="IMakerRepository.GetByUserIdAsync"/> from
/// the session-bound user; a user with no maker row gets
/// <see cref="BusinessErrorMessage.MakerNotFound"/>. (2) The EF projection
/// re-filters on <c>o.MakerId == maker.Id</c>. T-0081-verbatim.
/// </para>
/// </summary>
public static class GetMakerPayouts
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public sealed record Query(int Page = 1, int PageSize = DefaultPageSize)
        : IQuery<GetMakerPayoutsResponse>;

    /// <summary>Globally-unique wrapper name to avoid the NSwag TS class collision.</summary>
    public sealed record GetMakerPayoutsResponse(PagedData<MakerPayoutListItemDto> Payouts);

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
        }
    }

    public sealed class Handler(
        IPayoutQueries payoutQueries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerPayoutsResponse>>
    {
        public async Task<BusinessResult<GetMakerPayoutsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerPayoutsResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<GetMakerPayoutsResponse>(
                    Error.NotFound("maker", BusinessErrorMessage.MakerNotFound));
            }

            var paged = await payoutQueries.GetMakerPayoutsPagedAsync(
                maker.Id, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetMakerPayoutsResponse(paged));
        }
    }
}
