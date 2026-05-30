using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Catalog;

/// <summary>
/// Public catalog maker list (US-customer-0007). Anonymous — no session
/// needed. Delegates the projection to <see cref="ICatalogQueries"/>
/// (the EF/LINQ lives in Infra; this handler just validates the paging
/// inputs and forwards the filter).
///
/// <para>
/// Page size is clamped to <see cref="MaxPageSize"/> so a client can't
/// request a 10,000-row page. Default page size is
/// <see cref="DefaultPageSize"/> (24 per AC-1).
/// </para>
/// </summary>
public static class GetPagedMakers
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;

    public sealed record Query(
        string CountryCode,
        string? CategorySlug,
        string? City,
        int? MinRatingStars,
        int Page,
        int PageSize)
        : IQuery<PagedData<MakerListItem>>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.CountryCode)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            // Upper-bound Page so (Page-1)*PageSize can't overflow Int32
            // into a negative Skip offset (T-0043 Copilot review). The
            // cap is far beyond any real catalog depth.
            RuleFor(q => q.Page)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize).WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .InclusiveBetween(1, MaxPageSize).WithErrorCode(BusinessErrorMessage.MinValue);

            When(q => q.MinRatingStars is not null, () =>
            {
                RuleFor(q => q.MinRatingStars!.Value)
                    .InclusiveBetween(1, 5).WithErrorCode(BusinessErrorMessage.MinValue);
            });
        }
    }

    public sealed class Handler(ICatalogQueries catalog)
        : IRequestHandler<Query, BusinessResult<PagedData<MakerListItem>>>
    {
        public async Task<BusinessResult<PagedData<MakerListItem>>> Handle(Query query, CancellationToken cancellationToken)
        {
            var result = await catalog.GetPagedMakersAsync(
                new CatalogFilter(
                    CountryCode: query.CountryCode,
                    CategorySlug: query.CategorySlug,
                    City: query.City,
                    MinRatingStars: query.MinRatingStars,
                    Page: query.Page,
                    PageSize: query.PageSize),
                cancellationToken);

            return BusinessResult.Success(result);
        }
    }
}
