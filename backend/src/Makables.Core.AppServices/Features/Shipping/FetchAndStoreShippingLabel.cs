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
/// T-0074. Fetch the Packeta shipping-label PDF for an order's shipment
/// and store it in blob storage at the deterministic path
/// <c>invoices/{cc}/orders/{orderId}/label.pdf</c>. Idempotent — a
/// HEAD-check short-circuits when the blob already exists, sparing the
/// Packeta API quota on retries.
///
/// <para>
/// Dispatched by <c>GenerateLabelFunction</c> off the <c>generate-label</c>
/// queue. The Function is a thin queue-trigger wrapper (mirror of T-0069
/// <c>GenerateInvoiceFunction</c>); all logic lives here.
/// </para>
///
/// <para>
/// <b>No Order state mutation.</b> T-0072 already set
/// <see cref="Orders.Order.ShippingCarrierRef"/> /
/// <see cref="Orders.Order.ShippingCarrierTrackingUrl"/> + transitioned
/// to <see cref="OrderState.Shipped"/>. The blob's existence at the
/// deterministic path IS the only state this handler produces.
/// T-0075's download endpoint reads the blob (with a live-Packeta
/// fallback on cache-miss).
/// </para>
/// </summary>
public static class FetchAndStoreShippingLabel
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(string BlobPath);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrderId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(40).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IOrderRepository orders,
        IShippingCarrierFactory carrierFactory,
        IBlobStorageClient blobStorage,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command request, CancellationToken cancellationToken)
        {
            // Step 1: Load the order unscoped + read-only (Function
            // context has no user identity — same pattern as
            // IssueInvoice). This handler only inspects
            // ShippingCarrierRef + CountryCode and never mutates the
            // Order, so the AsNoTracking variant saves change-tracking
            // overhead per ADR 0025 §Performance expectations item 2.
            var order = await orders.GetByIdUnscopedReadOnlyAsync(request.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Verify carrier ref present — guardrail. T-0072
            // should have set it before enqueueing.
            if (string.IsNullOrWhiteSpace(order.ShippingCarrierRef))
            {
                logger.LogError(
                    "FetchAndStoreShippingLabel: order {OrderId} has no ShippingCarrierRef — refusing to fetch.",
                    order.Id);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            // Step 3: Compute the deterministic blob path. Lower-cased
            // country code per ADR 0011 + T-0068b precedent.
            var cc = order.CountryCode.ToLowerInvariant();
            var blobPath = $"{cc}/orders/{order.Id}/label.pdf";

            // Step 4: HEAD-check idempotency. If the blob exists we skip
            // the Packeta fetch entirely — sparing the API quota on
            // queue retries and re-deliveries.
            var existsResult = await blobStorage.ExistsAsync(
                BlobContainer.Invoices, blobPath, cancellationToken);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                logger.LogInformation(
                    "FetchAndStoreShippingLabel: blob already exists at {BlobPath} (idempotent skip).",
                    blobPath);
                return BusinessResult.Success(new Response(blobPath));
            }

            // Step 5: Resolve carrier.
            var carrierResult = await carrierFactory.ResolveAsync(order.CountryCode, cancellationToken);
            if (!carrierResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(carrierResult.Error!);
            }

            // Step 6: Fetch the label PDF stream.
            var pdfResult = await carrierResult.Value!.GetLabelPdfAsync(
                order.ShippingCarrierRef!, cancellationToken);
            if (!pdfResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(pdfResult.Error!);
            }

            // Step 7: Upload the PDF to blob storage. Stream disposal
            // happens via the using/await using below — the Packeta
            // adapter returned an owning stream.
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
                    "FetchAndStoreShippingLabel: blob upload threw for order {OrderId} at {BlobPath}.",
                    order.Id, blobPath);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.ShippingCarrierConfigurationError));
            }

            logger.LogInformation(
                "FetchAndStoreShippingLabel: stored label for order {OrderId} at {BlobPath}.",
                order.Id, blobPath);
            return BusinessResult.Success(new Response(blobPath));
        }
    }
}
