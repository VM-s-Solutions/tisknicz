using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using MediatR;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Records an already-uploaded image on a product. The controller does
/// the multipart upload to blob storage FIRST (type + size validation,
/// magic-byte sniffing per ADR 0011), then sends the resulting
/// <see cref="Command.BlobPath"/> here to attach it to the aggregate.
///
/// <para>
/// Splitting upload (controller) from attach (this command) keeps the
/// blob I/O out of the MediatR pipeline's unit-of-work transaction —
/// a blob write that succeeds while the DB commit fails leaves an
/// orphan blob (cheap), not a dangling DB row pointing at a missing
/// blob.
/// </para>
///
/// <para>
/// Image-cap is pre-checked here (not left to the entity's throw) so
/// the maker gets a typed <see cref="BusinessErrorMessage.ProductImageLimitReached"/>.
/// Ownership / IDOR: same shield as UpdateProduct.
/// </para>
/// </summary>
public static class AddProductImage
{
    public sealed record Command(string ProductId, string BlobPath) : ICommand<Response>;

    public sealed record Response(string ImageId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.BlobPath)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(1024).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IProductRepository products,
        IMakerRepository makers,
        IUserSessionProvider session,
        IIdGenerator ids)
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            var userId = session.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            var maker = await makers.GetByUserIdAsync(userId, cancellationToken);
            if (maker is null)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("maker"));
            }

            var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
            if (product is null || product.MakerId != maker.Id)
            {
                return BusinessResult.Failure<Response>(Error.NotFound("product"));
            }

            if (product.Images.Count >= Product.MaxImageCount)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("images", BusinessErrorMessage.ProductImageLimitReached));
            }

            var image = product.AddImage(ids.Next(), command.BlobPath);

            return BusinessResult.Success(new Response(image.Id));
        }
    }
}
