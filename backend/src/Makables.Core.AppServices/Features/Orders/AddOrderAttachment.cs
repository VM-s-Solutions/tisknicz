using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using MediatR;

namespace Makables.Core.AppServices.Features.Orders;

/// <summary>
/// Records an already-uploaded attachment on a customer order. The
/// controller does the multipart upload to blob storage FIRST (type +
/// size validation, magic-byte sniff per ADR 0011), then sends the
/// resulting <see cref="Command.BlobPath"/> here to attach it to the
/// aggregate.
///
/// <para>
/// Mirrors <see cref="Products.AddProductImage"/>: blob I/O sits outside
/// the MediatR unit-of-work transaction so a blob write that succeeds
/// while the DB commit fails leaves an orphan blob (cheap, the upload
/// controller best-effort deletes it) rather than a dangling DB row that
/// points at a missing blob.
/// </para>
///
/// <para>
/// State-gate + count-cap are re-checked here under the UoW transaction.
/// The controller's checks are optimistic — two concurrent uploads could
/// both pass the controller's count check and both reach the handler;
/// the second one's <see cref="Order.AddAttachment"/> call sees the
/// updated count and returns <see cref="BusinessErrorMessage.OrderAttachmentLimitReached"/>.
/// </para>
/// </summary>
public static class AddOrderAttachment
{
    public sealed record Command(
        string OrderId,
        string BlobPath,
        string OriginalFilename,
        string ContentType,
        long SizeBytes) : ICommand<Response>;

    public sealed record Response(
        string AttachmentId,
        string OriginalFilename,
        long SizeBytes,
        DateTimeOffset UploadedOn);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.BlobPath)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(OrderAttachment.MaxBlobPathLength).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.OriginalFilename)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(OrderAttachment.MaxOriginalFilenameLength).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.ContentType)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(OrderAttachment.MaxContentTypeLength).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.SizeBytes)
                .GreaterThan(0).WithErrorCode(BusinessErrorMessage.MinValue);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IUserSessionProvider session,
        IIdGenerator ids,
        IClock clock)
        : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(Command command, CancellationToken cancellationToken)
        {
            // Step 1: resolve customer from session.
            var customerUserId = session.GetUserId();
            if (string.IsNullOrEmpty(customerUserId))
            {
                return BusinessResult.Failure<Response>(Error.Unauthorized());
            }

            // Step 2: load the order (ownership-scoped, IDOR-shielded —
            // null for both unknown ids AND cross-customer ids).
            var order = await orders.GetByIdForCustomerAsync(command.OrderId, customerUserId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 3 + 4: re-check state-gate and count-cap inside the UoW.
            // Order.AddAttachment surfaces the same codes the controller's
            // optimistic checks return; the race-loser path is here.
            if (!order.AllowsAttachmentUpload())
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("order", BusinessErrorMessage.OrderStateForbidsAttachment));
            }
            if (order.Attachments.Count >= Order.MaxAttachmentCount)
            {
                return BusinessResult.Failure<Response>(
                    Error.Conflict("attachments", BusinessErrorMessage.OrderAttachmentLimitReached));
            }

            // Step 5: build the child entity. OrderAttachment.Create
            // validates the per-row invariants; by this point the
            // controller has already filtered the user-input shape.
            var attachmentId = ids.Next();
            var attachment = OrderAttachment.Create(
                id: attachmentId,
                orderId: order.Id,
                blobPath: command.BlobPath,
                originalFilename: command.OriginalFilename,
                contentType: command.ContentType,
                sizeBytes: command.SizeBytes,
                uploadedByUserId: customerUserId,
                countryCode: order.CountryCode);

            // Step 6: append via the aggregate. The state + count guards
            // here are defence-in-depth — they should already have passed
            // the steps above, but the race window between the read and
            // the write inside this handler is what AddAttachment is for.
            var addResult = order.AddAttachment(attachment);
            if (!addResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(addResult.Error!);
            }

            // Step 7: no explicit repository call — EF change-tracking on
            // the loaded order persists the new child through the
            // aggregate. UnitOfWorkPipelineBehavior commits.

            // Step 8: stamp the upload timestamp via IClock so tests can
            // pin time; matches the wire shape AC-1 promises.
            var uploadedOn = clock.UtcNow;

            return BusinessResult.Success(new Response(
                AttachmentId: attachmentId,
                OriginalFilename: attachment.OriginalFilename,
                SizeBytes: attachment.SizeBytes,
                UploadedOn: uploadedOn));
        }
    }
}
