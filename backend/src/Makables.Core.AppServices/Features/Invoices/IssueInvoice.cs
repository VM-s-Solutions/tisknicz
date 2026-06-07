using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Rendering;
using Makables.Core.Domain.Storage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.Invoices;

/// <summary>
/// Issue a customer-facing invoice for a paid <see cref="Order"/>:
/// snapshot the issuer + recipient + money + DUZP onto a new
/// <see cref="Invoice"/> row, render the PDF via
/// <see cref="IInvoicePdfRenderer"/>, upload it to blob storage, and
/// attach the blob path via <see cref="Invoice.AttachPdfBlobPath"/>.
///
/// <para>
/// <b>Dispatch model.</b> The T-0069 <c>GenerateInvoiceFunction</c>
/// pulls <see cref="Outbox.OutboxEventTypes.InvoiceGenerate"/> messages
/// off the outbox queue and dispatches this command via Mediator. The
/// handler is the only piece that touches the renderer + blob client;
/// the Function is a thin queue-trigger boundary. T-0068b locked
/// decision 7.
/// </para>
///
/// <para>
/// <b>Idempotency (AC-5).</b> Step 3 short-circuits when an
/// <see cref="Invoice"/> already exists for the order — webhook
/// re-delivery sees the existing values, no second number allocated,
/// no second render, no second blob upload. Because the renderer is
/// deterministic (T-0068a locked decision 5), an idempotent retry that
/// did slip past the pre-check would still write byte-identical PDF
/// bytes to the same blob path, but the pre-check saves us the work.
/// </para>
///
/// <para>
/// <b>Mode coverage at T-0068b (AC-3 / AC-4).</b> Locked decision 6:
/// <see cref="InvoicingMode.None"/> + <see cref="InvoicingMode.StandardVat"/>
/// ship; <see cref="InvoicingMode.ReverseCharge"/> +
/// <see cref="InvoicingMode.StrictFiscalReporting"/> short-circuit at
/// step 4 with <see cref="BusinessErrorMessage.InvoicingModeNotImplemented"/>
/// before any persistence — no orphan invoice row, no orphan blob.
/// </para>
/// </summary>
public static class IssueInvoice
{
    public sealed record Command(string OrderId) : ICommand<Response>;

    public sealed record Response(
        string InvoiceId,
        string InvoiceNumber,
        string PdfBlobPath);

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
        ICountryConfigurationRepository countries,
        IInvoiceRepository invoices,
        IInvoiceNumberGenerator invoiceNumbers,
        IInvoicePdfRenderer renderer,
        IBlobStorageClient blobStorage,
        IIdGenerator ids,
        IClock clock,
        ILogger<Handler> logger) : IRequestHandler<Command, BusinessResult<Response>>
    {
        public async Task<BusinessResult<Response>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // Step 1: Load the order. Unscoped because the dispatcher is
            // the outbox processor — no caller principal, no audience
            // scope. Same shape as MarkOrderPaid (T-0067) which is also
            // dispatched off the webhook + processor boundary.
            var order = await orders.GetByIdUnscopedAsync(command.OrderId, cancellationToken);
            if (order is null)
            {
                return BusinessResult.Failure<Response>(
                    Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
            }

            // Step 2: Load CountryConfiguration. The issuer snapshot
            // (name + IČO + DIČ) + the SPAYD platform IBAN + the
            // invoicing mode all come from this row.
            var country = await countries.GetByCodeAsync(order.CountryCode, cancellationToken);
            if (country is null)
            {
                logger.LogCritical(
                    "IssueInvoice: CountryConfiguration {CountryCode} not found for order {OrderId}. " +
                    "Data corruption — refusing to issue.",
                    order.CountryCode, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            // Step 3: Idempotency pre-check. Webhook re-delivery of the
            // MarkOrderPaid → outbox.invoice.generate flow lands here a
            // second time; the existing invoice's values are returned
            // verbatim, no second number allocation, no second render,
            // no second blob upload. AC-5.
            var existing = await invoices.GetByOrderIdAsync(order.Id, cancellationToken);
            if (existing is not null)
            {
                // Reviewer L-1 fold: the "stalled-mid-flow" branch
                // (existing != null AND PdfBlobPath empty) is unreachable
                // under the current UoW commit policy — UnitOfWorkPipelineBehavior
                // only commits on IsSuccess, so a render or blob failure after
                // Invoice.AddAsync rolls back the Invoice row AND the
                // InvoiceNumberGenerator sequence increment. The next outbox
                // re-delivery finds existing == null and starts fresh from
                // Step 4. The ContinuePartialIssuanceAsync helper that handled
                // it was deleted as dead code per CLAUDE.md "no dead code".
                //
                // TODO(if IssueInvoice.Command is later marked
                // IPersistOnFailureCommand for stronger invoice-row durability):
                // the empty-PdfBlobPath branch becomes reachable and needs the
                // re-render + re-upload path restored. Add tests at that point.
                // UoW commit policy guarantees PdfBlobPath is non-null when
                // the row exists — see the dead-code reasoning above.
                return BusinessResult.Success(new Response(
                    InvoiceId: existing.Id,
                    InvoiceNumber: existing.InvoiceNumber,
                    PdfBlobPath: existing.PdfBlobPath!));
            }

            // Step 4: InvoicingMode switch. Per T-0068b locked decision 6,
            // ReverseCharge + StrictFiscalReporting short-circuit BEFORE
            // any persistence — no Invoice row allocated, no number
            // burned, no blob written. Follow-up tickets ship their
            // templates.
            var mode = country.InvoicingMode;
            if (mode is InvoicingMode.ReverseCharge or InvoicingMode.StrictFiscalReporting)
            {
                logger.LogWarning(
                    "IssueInvoice: InvoicingMode {Mode} not implemented; refusing to issue invoice for order {OrderId}.",
                    mode, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.InvoicingModeNotImplemented));
            }

            // Step 5: Allocate the invoice number inside the surrounding
            // command's transaction per ADR 0009. Gap-free by mechanism
            // (a failure after this point rolls back without consuming
            // the number).
            var invoiceNumber = await invoiceNumbers.NextAsync(order.CountryCode, cancellationToken);

            // Step 6: Build the Invoice aggregate. Issuer values come
            // from CountryConfiguration (snapshot); recipient values from
            // Order; money breakdown derived from order.TotalAmountMinor
            // and the mode.
            //
            // For None mode: VAT line is zero (the platform is not VAT-
            // registered at MVP per T-0068a locked decision 2).
            // For StandardVat: derive net + VAT from
            // amountWithVatMinor + vatRateBp via half-up rounding (ADR 0003).
            //
            // DUZP per T-0068a locked decision 3: PaidAt converted to
            // country-local date for StandardVat; null for None (Doklad
            // o prodeji doesn't carry a DUZP under § 29).
            var dueDate = ComputeDueDate(order, country);
            var taxableSupplyDate = ComputeTaxableSupplyDate(order, country, mode);
            var (netMinor, vatMinor, grossMinor, vatRateBp) =
                ComputeMoneyBreakdown(order, mode, country);

            var invoice = Invoice.Issue(
                id: ids.Next(),
                invoiceNumber: invoiceNumber,
                type: InvoiceType.Customer,
                orderId: order.Id,
                payoutBatchId: null,
                makerId: order.MakerId,
                issuerName: country.IssuerName,
                issuerIco: country.IssuerIco,
                issuerDic: country.IssuerDic,
                issuerBankAccount: null,
                recipientName: order.ContactName,
                recipientEmail: order.ContactEmail,
                recipientTaxId: null,
                recipientVatId: null,
                issueDate: ComputeIssueDate(order, country),
                taxableSupplyDate: taxableSupplyDate,
                dueDate: dueDate,
                invoicingMode: mode,
                amountWithoutVatMinor: netMinor,
                vatRateBp: vatRateBp,
                vatAmountMinor: vatMinor,
                amountWithVatMinor: grossMinor,
                currency: order.Currency,
                countryCode: order.CountryCode);

            // Step 7: Persist. UoW pipeline commits at end of handler.
            await invoices.AddAsync(invoice, cancellationToken);

            // Step 8: Render the PDF. Catch the throw shapes from the
            // renderer (NotImplementedException for ReverseCharge /
            // StrictFiscalReporting — already short-circuited at step 4
            // so this would be a programmer error; any other throw is
            // a layout / font bug). Translate to InvoiceRenderFailed
            // Permanent so the outbox processor parks the row for admin
            // attention.
            byte[] pdfBytes;
            try
            {
                pdfBytes = await renderer.RenderAsync(invoice, country, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "IssueInvoice: PDF render failed for invoice {InvoiceNumber} (order {OrderId}).",
                    invoiceNumber, order.Id);
                return BusinessResult.Failure<Response>(
                    Error.Permanent(BusinessErrorMessage.InvoiceRenderFailed));
            }

            // Step 9: Upload to blob. Path per role/invoice.md:
            // invoices/{cc}/orders/{orderId}/{invoiceNumber}.pdf. Lower-
            // cased country prefix matches the ADR 0011 convention.
            var blobPath = BuildBlobPath(invoice);
            var uploadResult = await UploadPdfAsync(blobPath, pdfBytes, cancellationToken);
            if (!uploadResult.IsSuccess)
            {
                return BusinessResult.Failure<Response>(uploadResult.Error!);
            }

            // Step 10: Attach the blob path to the aggregate. Set-once;
            // tracked change committed by the UoW pipeline.
            var attachResult = invoice.AttachPdfBlobPath(blobPath);
            if (!attachResult.IsSuccess)
            {
                // Set-once collision — only reachable if a parallel
                // IssueInvoice raced past the idempotency pre-check
                // (impossible in practice because the outbox queue
                // serialises by aggregate id, but defence-in-depth).
                return BusinessResult.Failure<Response>(attachResult.Error!);
            }

            return BusinessResult.Success(new Response(
                InvoiceId: invoice.Id,
                InvoiceNumber: invoice.InvoiceNumber,
                PdfBlobPath: blobPath));
        }

        private async Task<BusinessResult> UploadPdfAsync(
            string blobPath, byte[] pdfBytes, CancellationToken cancellationToken)
        {
            try
            {
                using var content = new MemoryStream(pdfBytes, writable: false);
                var uploadResult = await blobStorage.UploadAsync(
                    container: BlobContainer.Invoices,
                    path: blobPath,
                    content: content,
                    contentType: "application/pdf",
                    cancellationToken: cancellationToken);

                if (!uploadResult.IsSuccess)
                {
                    // Adapter already classified the failure; translate
                    // the typed Error to the invoice-specific code so
                    // admin triage sees "invoice blob upload" not "blob".
                    var kind = uploadResult.Error!.Type;
                    return BusinessResult.Failure(
                        kind == ErrorType.Transient
                            ? Error.Transient(BusinessErrorMessage.InvoiceBlobUploadFailed)
                            : Error.Permanent(BusinessErrorMessage.InvoiceBlobUploadFailed));
                }
                return BusinessResult.Success();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "IssueInvoice: Blob upload threw unexpectedly for path {BlobPath}.",
                    blobPath);
                return BusinessResult.Failure(
                    Error.Transient(BusinessErrorMessage.InvoiceBlobUploadFailed));
            }
        }

        /// <summary>
        /// Build the blob path per role/invoice.md:
        /// <c>invoices/{cc-lower}/orders/{orderId}/{invoiceNumber}.pdf</c>.
        /// The leading container segment is implicit (UploadAsync takes
        /// the container name separately) — this path is the in-container
        /// portion.
        /// </summary>
        private static string BuildBlobPath(Invoice invoice)
        {
            var cc = invoice.CountryCode.ToLowerInvariant();
            return $"{cc}/orders/{invoice.OrderId}/{invoice.InvoiceNumber}.pdf";
        }

        /// <summary>
        /// Issue date = order's PaidAt converted to country-local date.
        /// For the test edge case where PaidAt is unset (defensive — the
        /// outbox enqueue only fires after MarkOrderPaid sets PaidAt),
        /// fall back to the clock's UtcNow date.
        /// </summary>
        private DateOnly ComputeIssueDate(Order order, CountryConfiguration country)
        {
            var instant = order.PaidAt ?? clock.UtcNow;
            return ToCountryLocalDate(instant, country.TimeZoneId);
        }

        /// <summary>
        /// DUZP — datum uskutečnění zdanitelného plnění. Per T-0068a
        /// locked decision 3 + § 29 zákon o DPH:
        /// <list type="bullet">
        ///   <item><see cref="InvoicingMode.StandardVat"/> →
        ///     <c>order.PaidAt</c> in country-local date.</item>
        ///   <item><see cref="InvoicingMode.None"/> → null (Doklad o
        ///     prodeji doesn't carry a DUZP).</item>
        /// </list>
        /// </summary>
        private DateOnly? ComputeTaxableSupplyDate(
            Order order, CountryConfiguration country, InvoicingMode mode)
        {
            if (mode == InvoicingMode.None) return null;
            var instant = order.PaidAt ?? clock.UtcNow;
            return ToCountryLocalDate(instant, country.TimeZoneId);
        }

        /// <summary>
        /// Due date = issue date + 14 days (Czech practice for retail
        /// invoices). Held inline here pending a downstream ticket that
        /// pulls the window from a CountryConfiguration column.
        /// </summary>
        private DateOnly ComputeDueDate(Order order, CountryConfiguration country)
        {
            var issue = ComputeIssueDate(order, country);
            return issue.AddDays(14);
        }

        private static DateOnly ToCountryLocalDate(DateTimeOffset instant, string timeZoneId)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var local = TimeZoneInfo.ConvertTime(instant, tz);
            return DateOnly.FromDateTime(local.DateTime);
        }

        /// <summary>
        /// Derive (net, VAT, gross, rateBp) from the order's
        /// <see cref="Order.TotalAmountMinor"/> + the mode.
        /// <list type="bullet">
        ///   <item><see cref="InvoicingMode.None"/> → gross = total,
        ///     VAT = 0, rate = 0. Net = gross.</item>
        ///   <item><see cref="InvoicingMode.StandardVat"/> → gross =
        ///     total, rate = country.StandardVatRateBp; VAT computed
        ///     half-up from gross / (1 + rate); net = gross - VAT.
        ///     Balance is exact (ADR 0003).</item>
        /// </list>
        /// </summary>
        private static (long Net, long Vat, long Gross, int RateBp) ComputeMoneyBreakdown(
            Order order, InvoicingMode mode, CountryConfiguration country)
        {
            var gross = order.TotalAmountMinor;
            if (mode == InvoicingMode.None)
            {
                return (Net: gross, Vat: 0, Gross: gross, RateBp: 0);
            }

            var rateBp = country.StandardVatRateBp;
            // Net = gross * 10000 / (10000 + rateBp), VAT = gross - Net.
            // Half-up rounding per ADR 0003. integer math throughout.
            var numerator = gross * 10_000L;
            var denominator = 10_000L + rateBp;
            // Half-up: add denominator/2 to the numerator then integer-divide.
            var net = (numerator + denominator / 2) / denominator;
            var vat = gross - net;
            return (Net: net, Vat: vat, Gross: gross, RateBp: rateBp);
        }
    }
}
