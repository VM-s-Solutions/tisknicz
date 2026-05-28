using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Products;
using Makables.Core.Domain.Storage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Products;

/// <summary>
/// Removes an image from a product and deletes the underlying blob.
///
/// <para>
/// Order of operations: the entity removal happens first (it'll commit
/// via the unit-of-work pipeline on success), then the blob delete runs
/// as a best-effort side-effect. If the blob delete fails the DB row is
/// already gone — an orphan blob is cheap and a future GC sweep can
/// reconcile. We do NOT fail the command on a blob-delete error, because
/// the user-visible outcome (image no longer attached) has succeeded.
/// </para>
///
/// <para>Ownership / IDOR: same shield as UpdateProduct.</para>
/// </summary>
public static class RemoveProductImage
{
    public sealed record Command(string ProductId, string ImageId) : ICommand;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ProductId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.ImageId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IProductRepository products,
        IMakerRepository makers,
        IBlobStorageClient blobs,
        IUserSessionProvider session,
        ILogger<Handler> logger)
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

            var removed = product.RemoveImage(command.ImageId);
            if (removed is null)
            {
                return BusinessResult.Failure(
                    Error.NotFound("productImage"));
            }

            // Best-effort blob delete. The entity removal already
            // committed (well, will commit via the pipeline); a failure
            // here leaves an orphan blob, not a dangling row.
            var deleteResult = await blobs.DeleteAsync(
                BlobContainer.ProductImages, removed.BlobPath, cancellationToken);
            if (!deleteResult.IsSuccess)
            {
                logger.LogWarning(
                    "Product image {ImageId} removed from product {ProductId} but blob delete failed ({BlobPath}). Orphan blob left for GC.",
                    command.ImageId, command.ProductId, removed.BlobPath);
            }

            return BusinessResult.Success();
        }
    }
}
