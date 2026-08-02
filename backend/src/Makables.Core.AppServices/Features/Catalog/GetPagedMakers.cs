using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
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
/// <see cref="DefaultPageSize"/> (24 per AC-1). The category filter is
/// multi-select (OR semantics) and capped at
/// <see cref="MaxCategoryFilters"/> slugs so a caller can't force an
/// unbounded IN list.
/// </para>
/// </summary>
public static class GetPagedMakers
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;

    /// <summary>
    /// Upper bound on selected category slugs. Comfortably above any
    /// realistic category count while keeping the generated IN list
    /// bounded.
    /// </summary>
    public const int MaxCategoryFilters = 20;

    public sealed record Query(
        string CountryCode,
        IReadOnlyList<string>? CategorySlugs,
        string? City,
        int? MinRatingStars,
        MakerLegalType? LegalType,
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

            When(q => q.CategorySlugs is not null, () =>
            {
                RuleFor(q => q.CategorySlugs!.Count)
                    .LessThanOrEqualTo(MaxCategoryFilters).WithErrorCode(BusinessErrorMessage.MinValue);
            });

            When(q => q.LegalType.HasValue, () =>
            {
                RuleFor(q => q.LegalType!.Value)
                    .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
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
                    CategorySlugs: query.CategorySlugs,
                    City: query.City,
                    MinRatingStars: query.MinRatingStars,
                    LegalType: query.LegalType,
                    Page: query.Page,
                    PageSize: query.PageSize),
                cancellationToken);

            return BusinessResult.Success(result);
        }
    }
}
