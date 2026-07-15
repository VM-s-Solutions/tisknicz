using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Catalog;

/// <summary>
/// Public active-category list (T-0119). Anonymous — feeds the catalog
/// filter dropdown and the maker product-creation form, replacing the
/// frontend's hardcoded launch-category list so admin-created
/// categories actually surface. Delegates to
/// <see cref="ICategoryQueries"/> (EF/LINQ stays in Infra).
/// </summary>
public static class GetPublicCategories
{
    public sealed record Query(string CountryCode) : IQuery<GetPublicCategoriesResponse>;

    // Globally-unique response name (T-0080 naming lock — see GetAdminCategories).
    public sealed record GetPublicCategoriesResponse(IReadOnlyList<PublicCategoryItem> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.CountryCode)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.InvalidEnumValue);
        }
    }

    public sealed class Handler(ICategoryQueries categories)
        : IRequestHandler<Query, BusinessResult<GetPublicCategoriesResponse>>
    {
        public async Task<BusinessResult<GetPublicCategoriesResponse>> Handle(Query query, CancellationToken cancellationToken)
        {
            var items = await categories.GetActiveAsync(query.CountryCode, cancellationToken);
            return BusinessResult.Success(new GetPublicCategoriesResponse(items));
        }
    }
}
