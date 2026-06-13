using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Payouts;

/// <summary>
/// Scheduler for the weekly maker payout batch (US-admin-0007). Thin ADR
/// 0020 wrapper — it dispatches the parameterless
/// <see cref="CreatePayoutBatch.Command"/> via <see cref="ISender"/>,
/// interprets the <see cref="BusinessResult{T}"/>, and logs. Every business
/// decision (claim predicate, exclusions, fee invoices, batch numbering,
/// CSV) lives in the writer (T-0102a/b).
///
/// <para>
/// Dual trigger (ProcessOutboxFunction precedent): a timer
/// (<c>%RunWeeklyPayoutBatch:Schedule%</c>, default Monday 02:00 UTC,
/// <c>UseMonitor = true</c>) plus an HTTP escape hatch
/// (<see cref="AuthorizationLevel.Function"/>) so ops can force a run.
/// Writer idempotency makes a timer + manual double-fire harmless (the open
/// batch is returned with <c>AlreadyExisted = true</c>).
/// </para>
///
/// <para>
/// Four response branches (§C): <b>created</b> → Information with totals +
/// both exclusion counts; <b>AlreadyExisted</b> → Information "open batch
/// returned"; <b>payoutBatch.empty</b> → Information ("no payable orders" —
/// a normal quiet week, NEVER Warning); <b>any other failure</b> → Error
/// with the code. The timer path never throws — the Error log is the alert
/// surface.
/// </para>
/// </summary>
public sealed class RunWeeklyPayoutBatchFunction(
    ISender mediator,
    ILogger<RunWeeklyPayoutBatchFunction> logger)
{
    public const string TimerFunctionName = "RunWeeklyPayoutBatchTimer";
    public const string HttpFunctionName = "RunWeeklyPayoutBatchHttp";

    [Function(TimerFunctionName)]
    public async Task RunTimer(
        [TimerTrigger("%RunWeeklyPayoutBatch:Schedule%", UseMonitor = true)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (timer.IsPastDue)
        {
            // A missed Monday tick still pays makers this morning.
            logger.LogInformation("RunWeeklyPayoutBatch timer is past due; running anyway.");
        }

        // No throw on business failure: the per-branch Error log is the
        // alert surface; a Function-level throw adds nothing (ProcessOutbox
        // precedent).
        await DispatchAndInterpretAsync(cancellationToken);
    }

    [Function(HttpFunctionName)]
    public async Task<IActionResult> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "payouts/run-batch")] HttpRequestData _,
        CancellationToken cancellationToken)
    {
        var result = await DispatchAndInterpretAsync(cancellationToken);

        if (result.IsSuccess)
        {
            return new OkObjectResult(result.Value);
        }

        // payoutBatch.empty → 422 (nothing to do, not an error); any other
        // code → 500. Function-key-only ops surface, not a public contract.
        if (result.Error!.Code == BusinessErrorMessage.PayoutBatchEmpty)
        {
            return new UnprocessableEntityObjectResult(result.Error);
        }
        return new ObjectResult(result.Error) { StatusCode = StatusCodes.Status500InternalServerError };
    }

    private async Task<BusinessResult<CreatePayoutBatch.CreatePayoutBatchResponse>> DispatchAndInterpretAsync(
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreatePayoutBatch.Command(), cancellationToken);

        if (result.IsSuccess)
        {
            var r = result.Value!;
            if (r.AlreadyExisted)
            {
                logger.LogInformation(
                    "RunWeeklyPayoutBatch: open Processing batch returned (idempotent re-fire): {BatchNumber}.",
                    r.BatchNumber);
            }
            else
            {
                logger.LogInformation(
                    "RunWeeklyPayoutBatch: created {BatchNumber} — {OrderCount} orders, {MakerCount} makers, " +
                    "total {TotalAmountMinor} {Currency}; excluded partially-refunded={ExcludedRefunded} orders, " +
                    "no-bank-account={ExcludedNoBankMakers} makers.",
                    r.BatchNumber, r.OrderCount, r.MakerCount, r.TotalAmountMinor, r.Currency,
                    r.ExcludedPartiallyRefundedOrderCount, r.ExcludedNoBankAccountMakerCount);
            }
            return result;
        }

        if (result.Error!.Code == BusinessErrorMessage.PayoutBatchEmpty)
        {
            // Normal quiet week — NOT a Warning (the writer records the
            // attempt in telemetry; no batch row).
            logger.LogInformation("RunWeeklyPayoutBatch: no payable orders this week.");
            return result;
        }

        logger.LogError(
            "RunWeeklyPayoutBatch: CreatePayoutBatch failed with {Code}.", result.Error.Code);
        return result;
    }
}
