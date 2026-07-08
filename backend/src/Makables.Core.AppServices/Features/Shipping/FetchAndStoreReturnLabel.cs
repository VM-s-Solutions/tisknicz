using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;
using Makables.Core.Domain.Storage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Shipping;

/// <summary>
/// T-0146. Fetch the Packeta reverse-shipment label PDF for a dispute and
/// store it in blob storage at the deterministic path
/// <c>invoices/{cc}/disputes/{disputeId}/return-label.pdf</c> — verbatim
/// reuse of <see cref="FetchAndStoreShippingLabel"/>'s cache→carrier→cache
/// shape (T-0074), pointed at the dispute-scoped path instead of the
/// order-scoped one.
///
/// <para>
/// Dispatched by the shared <c>GenerateLabelFunction</c> off the
/// <c>generate-label</c> queue when the outbox event type is
/// <see cref="Outbox.OutboxEventTypes.ShippingGenerateReturnLabel"/>.
/// </para>
///
/// <para>
/// <b>No Dispute mutation.</b> <c>GenerateReturnLabel.Handler</c> already
/// set <see cref="Dispute.ReturnCarrierRef"/> /
/// <see cref="Dispute.ReturnTrackingUrl"/>. The blob's existence at the
/// deterministic path IS the only state this handler produces.
/// </para>
/// </summary>
public static class FetchAndStoreReturnLabel
{
    public sealed record Command(string DisputeId) : ICommand<Response>;

    public sealed record Response(string BlobPath);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DisputeId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IDisputeRepository disputes,
        IShippingCarrierFactory carrierFactory,
        IBlobStorageClient blobStorage,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command request, CancellationToken cancellationToken)
        {
            // Step 1: unscoped read-only load (Function context has no
            // user identity — mirrors FetchAndStoreShippingLabel step 1).
            var dispute = await disputes.GetByIdUnscopedReadOnlyAsync(request.DisputeId, cancellationToken);
            if (dispute is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.OrderDisputeNotFound));
            }

            // Step 2: verify a return shipment exists.
            if (string.IsNullOrWhiteSpace(dispute.ReturnCarrierRef))
            {
                logger.LogError(
                    "FetchAndStoreReturnLabel: dispute {DisputeId} has no ReturnCarrierRef — refusing to fetch.",
                    dispute.Id);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            // Step 3: deterministic dispute-scoped blob path (T-0146
            // Technical notes — same invoices/ container as the forward label).
            var cc = dispute.CountryCode.ToLowerInvariant();
            var blobPath = $"{cc}/disputes/{dispute.Id}/return-label.pdf";

            // Step 4: HEAD-check idempotency.
            var existsResult = await blobStorage.ExistsAsync(
                BlobContainer.Invoices, blobPath, cancellationToken);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                logger.LogInformation(
                    "FetchAndStoreReturnLabel: blob already exists at {BlobPath} (idempotent skip).",
                    blobPath);
                return BusinessResult.Success(new Response(blobPath));
            }

            // Step 5: resolve carrier.
            var carrierResult = await carrierFactory.ResolveAsync(dispute.CountryCode, cancellationToken);
            if (!carrierResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(carrierResult.Error!);
            }

            // Step 6: fetch the label PDF stream.
            var pdfResult = await carrierResult.Value!.GetLabelPdfAsync(
                dispute.ReturnCarrierRef!, cancellationToken);
            if (!pdfResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(pdfResult.Error!);
            }

            // Step 7: upload to blob storage.
            try
            {
                await using var stream = pdfResult.Value!;
                var uploadResult = await blobStorage.UploadAsync(
                    container: BlobContainer.Invoices,
                    path: blobPath,
                    content: stream,
                    contentType: "application/pdf",
                    cancellationToken: cancellationToken);
                if (!uploadResult.IsSuccess)
                {
                    var kind = uploadResult.Error!.Type;
                    return BusinessResult.Failure<Response>(
                        kind == ErrorType.Transient
                            ? Error.Transient(BusinessErrorMessage.ShippingCarrierUnavailable)
                            : Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "FetchAndStoreReturnLabel: blob upload threw for dispute {DisputeId} at {BlobPath}.",
                    dispute.Id, blobPath);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            logger.LogInformation(
                "FetchAndStoreReturnLabel: stored return label for dispute {DisputeId} at {BlobPath}.",
                dispute.Id, blobPath);
            return BusinessResult.Success(new Response(blobPath));
        }
    }
}
