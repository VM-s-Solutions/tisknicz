using Makables.Core.AppServices.Features.Outbox;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Outbox;

/// <summary>
/// Thin trigger wrapper around <see cref="IOutboxDispatcher"/> per
/// ADR 0020 + T-0029. Two triggers as required by the ADR:
///
/// <list type="bullet">
///   <item><description><b>Timer</b>: <c>"%ProcessOutbox:Schedule%"</c> (default every 30 s per local.settings).
///     Combined with <c>maxConcurrentCalls = 1</c> in <c>host.json</c> this keeps the sweep single-threaded.</description></item>
///   <item><description><b>HTTP</b>: <c>POST /api/outbox/process</c> with <c>AuthorizationLevel.Function</c>
///     so admins can force a sweep from the ops dashboard ("Process outbox now").</description></item>
/// </list>
///
/// All orchestration is in <see cref="IOutboxDispatcher.DispatchDueAsync"/>;
/// this class only triggers it and shapes the HTTP response. Per ADR
/// 0020 §"Compliance / verification": <em>Functions are thin wrappers;
/// Mediator does the work. No business logic in Makables.Functions/*.cs.</em>
/// </summary>
public sealed class ProcessOutboxFunction(
    IOutboxDispatcher dispatcher,
    ILogger<ProcessOutboxFunction> logger)
{
    public const string TimerFunctionName = "ProcessOutboxTimer";
    public const string HttpFunctionName = "ProcessOutboxHttp";

    [Function(TimerFunctionName)]
    public async Task RunTimer(
        [TimerTrigger("%ProcessOutbox:Schedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (timer.IsPastDue)
        {
            logger.LogInformation("ProcessOutbox timer is past due; running anyway.");
        }
        var summary = await dispatcher.DispatchDueAsync(cancellationToken);
        // No throw: the dispatcher already records per-row failures on the
        // outbox itself; a Function-level throw would mark the whole tick
        // failed in Application Insights for no operational benefit.
        logger.LogInformation(
            "ProcessOutboxTimer tick: loaded={Loaded} routed={Routed} stalled={Stalled} failedToPublish={FailedToPublish}",
            summary.Loaded, summary.Routed, summary.Stalled, summary.FailedToPublish);
    }

    [Function(HttpFunctionName)]
    public async Task<IActionResult> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "outbox/process")] HttpRequestData _,
        CancellationToken cancellationToken)
    {
        var summary = await dispatcher.DispatchDueAsync(cancellationToken);
        return new OkObjectResult(summary);
    }
}
