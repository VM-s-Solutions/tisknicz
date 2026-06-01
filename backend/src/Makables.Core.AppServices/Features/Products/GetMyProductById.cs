using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Maker dashboard single-product detail (T-0049 frontend consumer).
/// Powers the edit form — returns every field the
/// <c>UpdateProduct</c> command can mutate, plus the ordered image list.
///
/// <para>
/// <b>IDOR shield.</b> The owning maker is resolved from
/// <see cref="IUserSessionProvider.GetUserId"/>; the query then loads
/// the product only if it belongs to that maker. A product owned by
/// another maker — or a non-existent id — both return <c>product.notFound</c>
/// (same shape, no oracle that distinguishes "wrong owner" from
/// "doesn't exist"). Same pattern as <c>UpdateProduct</c> /
/// <c>AddProductImage</c>.
/// </para>
///
/// <para>
/// Includes soft-deleted products so the dashboard can edit (or display
/// info about) a deactivated item. The infra projection
/// <c>IgnoreQueryFilters()</c>s the global <see cref="Auditable"/>
/// filter to make that work.
/// </para>
/// </summary>
public static class GetMyProductById
{
    public sealed record Query(string ProductId) : IQuery<MakerProductDetail>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IMakerProductQueries queries,
        IMakerRepository makers,
        IUserSessionProvider session)
        : IRequestHandler<Query, BusinessResult<MakerProductDetail>>
    {
        public async Task<BusinessResult<MakerProductDetail>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<MakerProductDetail>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<MakerProductDetail>(Error.NotFound("maker"));
            }

            var detail = await queries.GetMyProductByIdAsync(
                maker.Id, query.ProductId, cancellationToken);
            if (detail is null)
            {
                return BusinessResult.Failure<MakerProductDetail>(Error.NotFound("product"));
            }

            return BusinessResult.Success(detail);
        }
    }
}
