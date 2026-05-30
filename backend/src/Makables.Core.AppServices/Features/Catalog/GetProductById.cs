using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Catalog;
using Makables.Core.Domain.Common;
using MediatR;

namespace Makables.Core.AppServices.Features.Catalog;

/// <summary>
/// Public product-detail query (US-customer-0009). Anonymous. Null
/// result (inactive product, or maker not publicly-listable) surfaces
/// as <see cref="BusinessErrorMessage.ProductNotFound"/> so a hidden
/// product isn't probeable by id.
/// </summary>
public static class GetProductById
{
    public sealed record Query(string ProductId) : IQuery<ProductDetail>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(ICatalogQueries catalog)
        : IRequestHandler<Query, BusinessResult<ProductDetail>>
    {
        public async Task<BusinessResult<ProductDetail>> Handle(Query query, CancellationToken cancellationToken)
        {
            var detail = await catalog.GetProductByIdAsync(query.ProductId, cancellationToken);
            if (detail is null)
            {
                return BusinessResult.Failure<ProductDetail>(Error.NotFound("product"));
            }

            return BusinessResult.Success(detail);
        }
    }
}
