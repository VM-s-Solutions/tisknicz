using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Maker soft-deletes a product (US-maker-0004 AC-4). The product
/// disappears from the public catalog via the global soft-delete query
/// filter; existing orders remain visible to all parties (they hold
/// the pricing snapshot from order time and reference the product by
/// FK regardless of <c>IsActive</c>).
///
/// <para>
/// Blob images are intentionally NOT deleted here — orphaning them is
/// cheap, and a hard-delete of the product (GDPR purge, future ticket)
/// will cascade the blobs. Soft-delete keeps the images addressable in
/// case the maker reactivates (also a future ticket).
/// </para>
///
/// <para>Ownership / IDOR: same shield as UpdateProduct.</para>
/// </summary>
public static class DeleteProduct
{
    public sealed record Command(string ProductId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IProductRepository products,
        IMakerRepository makers,
        IUserSessionProvider session,
        IClock clock)
        : IRequestHandler<Command, BusinessResult>
    {
        public async Task<BusinessResult> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure(Error.NotFound("maker"));
            }

            var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
            if (product is null || product.MakerId != maker.Id)
            {
                return BusinessResult.Failure(Error.NotFound("product"));
            }

            product.MarkDeactivated(userId, clock.UtcNow);

            return BusinessResult.Success();
        }
    }
}
