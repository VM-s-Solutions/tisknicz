using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Reviews;
using Makables.Core.Domain.Reviews.Queries;
using MediatR;

namespace Makables.Core.AppServices.Features.Reviews;

/// <summary>
/// Maker-host read: the maker-dashboard "reviews about me" list (T-0100
/// §C.6, US-maker-0014). Paginated 20/page. Maker-scoped projection —
/// cross-tenant probes return an empty page. Carries no customer identity
/// (GDPR data-minimization §C.7).
/// </summary>
public static class GetMakerReceivedReviews
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 20;

    public sealed record Query(int Page, int PageSize)
        : IQuery<GetMakerReceivedReviewsResponse>;

    public sealed record GetMakerReceivedReviewsResponse(PagedData<MakerReceivedReviewDto> Reviews);

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
        IReviewQueries reviews,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetMakerReceivedReviewsResponse>>
    {
        public async Task<BusinessResult<GetMakerReceivedReviewsResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<GetMakerReceivedReviewsResponse>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                // Maker-audience JWT without a maker row → empty page (no leak).
                return BusinessResult.Success(new GetMakerReceivedReviewsResponse(
                    PagedData<MakerReceivedReviewDto>.Empty(query.Page, query.PageSize)));
            }

            var page = await reviews.GetMakerReceivedReviewsPagedAsync(
                maker.Id, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(new GetMakerReceivedReviewsResponse(page));
        }
    }
}
