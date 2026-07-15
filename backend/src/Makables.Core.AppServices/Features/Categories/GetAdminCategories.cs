using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Categories;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Categories;

/// <summary>
/// Admin category list (T-0119 / US-admin-0013). Returns every category
/// INCLUDING deactivated rows so the admin can rename/reactivate and
/// see the full taxonomy. Thin wrapper over
/// <see cref="ICategoryQueries"/> — the admin-audience JWT on the
/// Web.Admin host is the authorization boundary (ADR 0013); the
/// session gate here is the same fail-closed check the category
/// mutations use.
/// </summary>
public static class GetAdminCategories
{
    public sealed record Query : IQuery<GetAdminCategoriesResponse>;

    // Globally-unique response name — a nested record named just `Response`
    // becomes an OpenAPI schema that shadows the DOM `Response` type in the
    // NSwag TS client (T-0080 naming lock).
    public sealed record GetAdminCategoriesResponse(IReadOnlyList<AdminCategoryItem> Items);

    public sealed class Handler(
        ICategoryQueries categories,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<GetAdminCategoriesResponse>>
    {
        public async Task<BusinessResult<GetAdminCategoriesResponse>> Handle(Query query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<GetAdminCategoriesResponse>(Error.Unauthorized());
            }

            var items = await categories.GetAllForAdminAsync(cancellationToken);
            return BusinessResult.Success(new GetAdminCategoriesResponse(items));
        }
    }
}
