using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Payouts;
using Makables.Core.Domain.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host payout-batch endpoints (US-admin-0007). <c>[Authorize]</c>
/// under the admin audience per ADR 0013 — a customer/maker JWT cannot
/// replay here. The create endpoint is the same code path the T-0104
/// weekly timer Function dispatches, keeping the admin "run batch now"
/// button and the cron on one writer (ADR 0014 audit).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payout-batches")]
[Authorize]
public sealed class PayoutBatchesController(
    IPayoutBatchRepository payoutBatches,
    IBlobStorageClient blobs) : MakablesApiController
{
    /// <summary>
    /// Create the weekly payout batch: claim every payout-eligible
    /// Delivered order for the default country into a new
    /// <c>Processing</c> batch, issue per-maker Fee invoices, build the
    /// bank CSV, and enqueue maker emails (T-0102a + T-0102b). Returns 200
    /// on both the created and the re-run (<c>AlreadyExisted = true</c>)
    /// paths (Silent-Success shape, US-admin-0007 AC-4). An empty week
    /// returns 409 <c>payoutBatch.empty</c>; a same-week re-run after
    /// completion returns 409 <c>payoutBatch.weekAlreadyProcessed</c>.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(CreatePayoutBatch.CreatePayoutBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        HandleResult(await Mediator.Send(new CreatePayoutBatch.Command(), ct));

    /// <summary>
    /// Count payout batches in a given state (T-0126 / Q-0027). Backs the admin
    /// overview's "Processing payouts" KPI tile (default
    /// <see cref="PayoutBatchState.Processing"/>). Read-only, admin-audience only
    /// (ADR 0013). Empty set → <c>{ count: 0 }</c>, never 404.
    /// </summary>
    [HttpGet("count")]
    [ProducesResponseType(typeof(GetProcessingPayoutsCount.GetProcessingPayoutsCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Count(
        [FromQuery] PayoutBatchState state = PayoutBatchState.Processing,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetProcessingPayoutsCount.Query(state), ct));

    /// <summary>Request body for <see cref="Complete"/>. The batch id rides the route.</summary>
    public sealed record MarkPayoutBatchCompletedRequest(string BankReference, DateOnly? PaymentDate);

    /// <summary>
    /// Settle a Processing batch: record the executed bank wire
    /// (<c>BankReference</c> + optional <c>PaymentDate</c>), move the batch to
    /// Completed, drive every claimed order to Completed, and enqueue one
    /// payout-sent email per maker — all in ONE UoW (T-0103, US-admin-0007
    /// AC-2). 200 on both the live-transition and the Silent-Success
    /// (<c>AlreadyCompleted = true</c>) re-call paths. 404
    /// <c>payoutBatch.notFound</c> for an unknown id; 409
    /// <c>payoutBatch.notProcessing</c> for a non-Processing/-Completed batch.
    /// A customer/maker JWT cannot replay here (admin audience).
    /// </summary>
    [HttpPost("{id}/complete")]
    [ProducesResponseType(typeof(MarkPayoutBatchCompleted.MarkPayoutBatchCompletedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        string id, [FromBody] MarkPayoutBatchCompletedRequest body, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new MarkPayoutBatchCompleted.Command(id, body.BankReference, body.PaymentDate), ct));

    /// <summary>
    /// Stream the batch's bank-transfer CSV (T-0102b §C.12). Controller-direct
    /// per the T-0088 precedent — byte streams don't fit the
    /// <c>BusinessResult&lt;T&gt;</c> envelope, so no MediatR query. Streams
    /// from the private <c>payouts</c> container through the admin host (ADR
    /// 0011 — no direct browser → blob link). 404 <c>payoutBatch.notFound</c>
    /// for an unknown id; 409 <c>payoutBatch.csvNotReady</c> when
    /// <c>CsvBlobPath</c> is still null. A customer/maker JWT cannot replay
    /// here (admin-audience enforcement on the host).
    /// </summary>
    [HttpGet("{id}/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadCsv(string id, CancellationToken ct)
    {
        var batch = await payoutBatches.GetByIdUnscopedAsync(id, ct);
        if (batch is null)
        {
            return NotFound(Error.NotFound("id", BusinessErrorMessage.PayoutBatchNotFound));
        }
        if (string.IsNullOrEmpty(batch.CsvBlobPath))
        {
            return Conflict(Error.Conflict("csv", BusinessErrorMessage.PayoutBatchCsvNotReady));
        }

        // CsvBlobPath is "{container}/{cc}/{batchNumber}.csv"; strip the
        // leading container segment for the DownloadAsync path argument.
        var path = StripContainerPrefix(batch.CsvBlobPath, BlobContainer.Payouts);
        var result = await blobs.DownloadAsync(BlobContainer.Payouts, path, ct);
        if (!result.IsSuccess)
        {
            // Blob-purged-but-row-remains race — honest 409 not-ready shape.
            return Conflict(Error.Conflict("csv", BusinessErrorMessage.PayoutBatchCsvNotReady));
        }

        var download = result.Value!;
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{batch.BatchNumber}.csv\"";
        return File(download.Content, "text/csv", enableRangeProcessing: false);
    }

    private static string StripContainerPrefix(string blobPath, string container)
    {
        var prefix = container + "/";
        return blobPath.StartsWith(prefix, StringComparison.Ordinal)
            ? blobPath[prefix.Length..]
            : blobPath;
    }
}
