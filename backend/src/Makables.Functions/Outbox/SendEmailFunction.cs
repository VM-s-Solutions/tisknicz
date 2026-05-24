using Makables.Core.AppServices.Features.Outbox;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Outbox;

/// <summary>
/// Storage-queue consumer for the <c>send-email</c> queue per ADR 0020
/// + T-0029. The queue message body is the outbox event id (string);
/// the function loads the row from Postgres (the system of record),
/// hands it to <see cref="ISendEmailHandler.HandleAsync"/>, and
/// returns without throwing on application-level failures.
///
/// <b>No re-throw on handled failure.</b> The handler classifies failures
/// against the outbox retry policy and writes the next-retry instant
/// directly on the row. Throwing here would also trigger Azure's queue
/// retry, double-billing the budget. The function only throws on
/// genuinely unexpected exceptions (DB connection lost, etc.) so Azure
/// can re-queue and the next instance picks up the same id.
/// </summary>
public sealed class SendEmailFunction(
    ISendEmailHandler handler,
    ILogger<SendEmailFunction> logger)
{
    public const string FunctionName = "SendEmail";

    [Function(FunctionName)]
    public async Task Run(
        [QueueTrigger("%OutboxQueues:SendEmailQueueName%", Connection = "AzureWebJobsStorage")]
        string outboxEventId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outboxEventId))
        {
            logger.LogWarning("SendEmail received empty queue message; discarding.");
            return;
        }

        var outcome = await handler.HandleAsync(outboxEventId, cancellationToken);
        logger.LogInformation(
            "SendEmail processed OutboxEvent {OutboxEventId}: outcome={Outcome}",
            outboxEventId, outcome);
    }
}
