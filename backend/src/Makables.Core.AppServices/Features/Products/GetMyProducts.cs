using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Maker dashboard product list (T-0049 frontend consumer). Owner-scoped
/// — returns the authenticated maker's products INCLUDING soft-deleted
/// ones so the dashboard can surface drafts and recently-deactivated
/// items, which the public catalog hides.
///
/// <para>
/// <b>IDOR shield.</b> The owning maker is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/> → <see cref="IMakerRepository.GetByUserIdAsync"/>.
/// There is no <c>MakerId</c> in the query shape, and no callable
/// "list anyone's products" path — same pattern as <c>CreateProduct</c>
/// / <c>UpdateProduct</c>. A user with no maker row gets <c>maker.notFound</c>.
/// </para>
///
/// <para>
/// Page size is clamped to <see cref="MaxPageSize"/> so a client can't
/// request a 10,000-row page (mirrors T-0043 <c>GetPagedMakers</c>).
/// Default <see cref="DefaultPageSize"/> matches the public catalog's
/// "24 per page" cadence so the dashboard feels uniform.
/// </para>
/// </summary>
public static class GetMyProducts
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;

    public sealed record Query(int Page, int PageSize) : IQuery<PagedData<MakerProductListItem>>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            // Upper-bound Page so (Page-1)*PageSize can't overflow Int32
            // into a negative Skip offset (T-0043 Copilot review).
            RuleFor(q => q.Page)
                .InclusiveBetween(1, int.MaxValue / MaxPageSize).WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(q => q.PageSize)
                .InclusiveBetween(1, MaxPageSize).WithErrorCode(BusinessErrorMessage.MinValue);
        }
    }

    public sealed class Handler(
        IMakerProductQueries queries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<PagedData<MakerProductListItem>>>
    {
        public async Task<BusinessResult<PagedData<MakerProductListItem>>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<PagedData<MakerProductListItem>>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<PagedData<MakerProductListItem>>(Error.NotFound("maker"));
            }

            var page = await queries.GetMyProductsAsync(
                maker.Id, query.Page, query.PageSize, cancellationToken);

            return BusinessResult.Success(page);
        }
    }
}
