using System.Text;
using System.Text.Json;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Makers;
using Makables.Core.Domain.Numbering;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Outbox;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Rendering;
using Makables.Core.Domain.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Core.AppServices.Features.Payouts;

/// <summary>
/// Generates a payout batch's financial artifacts (T-0102b §C.2–§C.4),
/// invoked by <see cref="CreatePayoutBatch.Handler"/> post-claim and on the
/// re-run path. Per-maker unit (atomic-in-order): issue one
/// <c>InvoiceType.Fee</c> invoice → render PDF → upload blob →
/// <c>AttachPdfBlobPath</c> → enqueue <c>payout.feeInvoice.makerEmail</c>.
/// CSV runs after ALL makers complete.
///
/// <para>
/// <b>Re-entrancy (§C.4).</b> On a re-run the service resumes: a batch
/// maker WITHOUT a Fee invoice → full unit; an existing Fee invoice with
/// <c>PdfBlobPath == null</c> → re-render + upload + attach + enqueue; a
/// null <see cref="PayoutBatch.CsvBlobPath"/> → format + upload + attach.
/// A fully-artifacted re-run is a no-op. Fee invoice numbers are allocated
/// once per maker per batch — never duplicated on the shared FV sequence.
/// </para>
///
/// <para>
/// <b>Failure posture (§C.3).</b> The first per-step failure aborts the
/// rest, logs Critical, and yields <c>Complete == false</c>; it NEVER
/// throws past itself (a throw would roll back the committed claim). The
/// handler still returns success; the re-run path completes the rest.
/// </para>
/// </summary>
public sealed class PayoutArtifactService(
    IOrderRepository orders,
    IInvoiceRepository invoices,
    IInvoiceNumberGenerator invoiceNumbers,
    IInvoicePdfRenderer renderer,
    IBlobStorageClient blobStorage,
    IMakerRepository makers,
    IUserRepository users,
    ICountryConfigurationRepository countries,
    IPayoutCsvFormatter csvFormatter,
    IOutbox outbox,
    ILanguageResolver languageResolver,
    IIdGenerator ids,
    IOptions<PublicAppUrlsOptions> publicAppUrls,
    ILogger<PayoutArtifactService> logger) : IPayoutArtifactService
{
    public async Task<PayoutArtifactResult> GenerateAsync(
        PayoutBatch batch,
        IReadOnlyList<Order>? claimedOrders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        try
        {
            var config = await countries.GetByCodeAsync(batch.CountryCode, cancellationToken);
            if (config is null)
            {
                logger.LogCritical(
                    "PayoutArtifactService: CountryConfiguration {CountryCode} missing for batch {BatchNumber}.",
                    batch.CountryCode, batch.BatchNumber);
                return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: 0, CsvReady: false);
            }

            // DUZP = batch creation (claim) date in country-local TZ (Q2).
            // CreatedAt is stamped by the auditable interceptor at commit, so
            // on the in-flight first run it is the default; use the batch's
            // CreatedAt when set, else "now" via the local-TZ conversion of
            // the row — deterministic per re-run because CreatedAt is fixed
            // after the first commit.
            var duzp = ToCountryLocalDate(
                batch.CreatedAt == default ? DateTimeOffset.UtcNow : batch.CreatedAt,
                config.TimeZoneId);
            var dueDate = duzp.AddDays(14);

            // First run: use the just-claimed in-memory orders (the
            // surrounding UoW has not committed them, so a fresh DB query
            // would see nothing). Re-run: read the committed claim.
            var batchOrders = claimedOrders
                ?? await orders.GetByPayoutBatchIdUnscopedAsync(batch.Id, cancellationToken);
            var existingFeeInvoices = await invoices.GetByPayoutBatchIdAsync(batch.Id, cancellationToken);
            var feeInvoiceByMaker = existingFeeInvoices.ToDictionary(i => i.MakerId, StringComparer.Ordinal);

            // Group claimed orders by maker — each group is one Fee invoice.
            var perMaker = batchOrders
                .GroupBy(o => o.MakerId, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var csvLines = new List<(string Company, string MakerId, PayoutCsvLine Line)>();
            var feeInvoiceCount = existingFeeInvoices.Count;

            foreach (var group in perMaker)
            {
                var makerId = group.Key;
                var makerOrders = group.ToList();

                var maker = await makers.GetByIdAsync(makerId, cancellationToken);
                if (maker is null || string.IsNullOrWhiteSpace(maker.BankAccount))
                {
                    // Should never happen — Q5 excluded NULL-bank makers at
                    // claim time. A null here is a data-integrity violation:
                    // log Critical and bail incomplete (re-run re-checks).
                    logger.LogCritical(
                        "PayoutArtifactService: claimed maker {MakerId} missing or has no bank account in batch {BatchNumber}.",
                        makerId, batch.BatchNumber);
                    return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                }

                var feeTotal = makerOrders.Sum(o => o.PlatformFeeAmountMinor);

                // Re-entrancy: reuse the existing Fee invoice for this maker
                // if one was already issued; otherwise issue a fresh one.
                if (!feeInvoiceByMaker.TryGetValue(makerId, out var feeInvoice))
                {
                    var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
                    if (makerUser is null)
                    {
                        logger.LogCritical(
                            "PayoutArtifactService: maker {MakerId} linked user {UserId} missing in batch {BatchNumber}.",
                            makerId, maker.UserId, batch.BatchNumber);
                        return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                    }

                    var invoiceNumber = await invoiceNumbers.NextAsync(batch.CountryCode, cancellationToken);
                    feeInvoice = Invoice.Issue(
                        id: ids.Next(),
                        invoiceNumber: invoiceNumber,
                        type: InvoiceType.Fee,
                        orderId: null,
                        // Many orders — their numbers ride on the line items.
                        orderNumber: null,
                        payoutBatchId: batch.Id,
                        makerId: maker.Id,
                        issuerName: config.IssuerName,
                        issuerIco: config.IssuerIco,
                        issuerDic: config.IssuerDic,
                        issuerBankAccount: null,
                        issuerAddress: config.IssuerAddress,
                        recipientName: maker.CompanyName,
                        recipientEmail: makerUser.Email,
                        recipientTaxId: maker.RegistrationNumber,
                        recipientVatId: maker.VatId,
                        issueDate: duzp,
                        taxableSupplyDate: config.InvoicingMode == InvoicingMode.None ? null : duzp,
                        dueDate: dueDate,
                        invoicingMode: config.InvoicingMode,
                        amountWithoutVatMinor: feeTotal,
                        vatRateBp: 0,
                        vatAmountMinor: 0,
                        amountWithVatMinor: feeTotal,
                        currency: batch.Currency,
                        countryCode: batch.CountryCode,
                        // The maker never transfers this — the payout below
                        // is already net of PlatformFeeAmountMinor. Settled
                        // on the DUZP, by deduction.
                        paidOn: duzp,
                        paymentMethod: SettlementMethods.PayoutDeduction);
                    await invoices.AddAsync(feeInvoice, cancellationToken);
                    feeInvoiceByMaker[makerId] = feeInvoice;
                    feeInvoiceCount++;
                }

                // Render + upload + attach + enqueue only when the PDF is not
                // yet present (re-entrancy: a fully-done maker is skipped, so
                // the renderer/blob mocks record zero calls — AC-8).
                if (string.IsNullOrWhiteSpace(feeInvoice.PdfBlobPath))
                {
                    var lineItems = makerOrders
                        .Select(o => new FeeInvoiceLineItem(o.OrderNumber, o.PlatformFeeAmountMinor))
                        .ToList();

                    byte[] pdfBytes;
                    try
                    {
                        pdfBytes = await renderer.RenderFeeAsync(feeInvoice, lineItems, config, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogCritical(ex,
                            "PayoutArtifactService: fee-invoice render failed for {InvoiceNumber} (maker {MakerId}, batch {BatchNumber}).",
                            feeInvoice.InvoiceNumber, makerId, batch.BatchNumber);
                        return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                    }

                    var pdfBlobPath = $"{batch.CountryCode.ToLowerInvariant()}/payouts/{batch.Id}/{feeInvoice.InvoiceNumber}.pdf";
                    var upload = await UploadAsync(BlobContainer.Invoices, pdfBlobPath, pdfBytes, "application/pdf", cancellationToken);
                    if (!upload)
                    {
                        return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                    }

                    var attach = feeInvoice.AttachPdfBlobPath(pdfBlobPath);
                    if (!attach.IsSuccess)
                    {
                        logger.LogCritical(
                            "PayoutArtifactService: AttachPdfBlobPath refused for {InvoiceNumber}: {Code}.",
                            feeInvoice.InvoiceNumber, attach.Error!.Code);
                        return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                    }

                    await EnqueueMakerEmailAsync(batch, feeInvoice, maker, feeTotal, cancellationToken);
                }

                csvLines.Add((maker.CompanyName, makerId, new PayoutCsvLine(
                    BankAccount: maker.BankAccount,
                    AmountMinor: makerOrders.Sum(o => o.MakerPayoutAmountMinor),
                    MakerCompanyName: maker.CompanyName)));
            }

            // CSV after all makers complete (deterministic order: company
            // name then maker id, §C.5).
            var csvReady = batch.CsvBlobPath is not null;
            if (batch.CsvBlobPath is null && csvLines.Count > 0)
            {
                var ordered = csvLines
                    .OrderBy(l => l.Company, StringComparer.Ordinal)
                    .ThenBy(l => l.MakerId, StringComparer.Ordinal)
                    .Select(l => l.Line)
                    .ToList();
                var csv = csvFormatter.Format(new PayoutCsvBatch(batch.BatchNumber, ordered));

                // UTF-8 WITH BOM (Czech bank tooling) — the artifact service
                // owns encoding, not the formatter.
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);
                var csvBlobPath = $"{batch.CountryCode.ToLowerInvariant()}/{batch.BatchNumber}.csv";
                var csvUpload = await UploadAsync(BlobContainer.Payouts, csvBlobPath, bytes, "text/csv", cancellationToken);
                if (!csvUpload)
                {
                    return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                }

                var attachCsv = batch.AttachCsvBlobPath($"{BlobContainer.Payouts}/{csvBlobPath}");
                if (!attachCsv.IsSuccess)
                {
                    logger.LogCritical(
                        "PayoutArtifactService: AttachCsvBlobPath refused for batch {BatchNumber}: {Code}.",
                        batch.BatchNumber, attachCsv.Error!.Code);
                    return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: feeInvoiceCount, CsvReady: false);
                }
                csvReady = true;
            }

            return new PayoutArtifactResult(Complete: csvReady, FeeInvoiceCount: feeInvoiceCount, CsvReady: csvReady);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never throw past the service (§C.3) — a throw would roll back
            // the committed claim. Log Critical, return incomplete; the
            // re-run path resumes.
            logger.LogCritical(ex,
                "PayoutArtifactService: unexpected failure generating artifacts for batch {BatchNumber}.",
                batch.BatchNumber);
            return new PayoutArtifactResult(Complete: false, FeeInvoiceCount: 0, CsvReady: false);
        }
    }

    private async Task EnqueueMakerEmailAsync(
        PayoutBatch batch, Invoice feeInvoice, Makables.Core.Domain.Makers.Maker maker, long feeTotal,
        CancellationToken cancellationToken)
    {
        var makerUser = await users.GetByIdAsync(maker.UserId, cancellationToken);
        if (makerUser is null)
        {
            logger.LogCritical(
                "PayoutArtifactService: maker {MakerId} user {UserId} missing at email enqueue (batch {BatchNumber}).",
                maker.Id, maker.UserId, batch.BatchNumber);
            return;
        }

        var language = await languageResolver.ResolveForUserAsync(makerUser, cancellationToken);
        var actionUrl = $"{publicAppUrls.Value.WebBaseUrl.TrimEnd('/')}/dashboard/maker/payouts";
        var payload = new PayoutFeeInvoiceMakerEmailPayload(
            InvoiceId: feeInvoice.Id,
            MakerId: maker.Id,
            MakerEmail: makerUser.Email,
            BatchNumber: batch.BatchNumber,
            InvoiceNumber: feeInvoice.InvoiceNumber,
            FeeAmountMinor: feeTotal,
            Currency: batch.Currency,
            ActionUrl: actionUrl,
            LanguageCode: language);

        outbox.Enqueue(
            aggregateId: batch.Id,
            eventType: OutboxEventTypes.PayoutFeeInvoiceMakerEmail,
            payloadJson: JsonSerializer.Serialize(payload));
    }

    private async Task<bool> UploadAsync(
        string container, string path, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new MemoryStream(bytes, writable: false);
            var result = await blobStorage.UploadAsync(container, path, content, contentType, cancellationToken);
            if (!result.IsSuccess)
            {
                logger.LogCritical(
                    "PayoutArtifactService: blob upload failed for {Container}/{Path}: {Code}.",
                    container, path, result.Error!.Code);
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "PayoutArtifactService: blob upload threw for {Container}/{Path}.", container, path);
            return false;
        }
    }

    private static DateOnly ToCountryLocalDate(DateTimeOffset instant, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
