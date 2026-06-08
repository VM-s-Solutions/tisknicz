using System.Text.Json;
using Makables.Core.AppServices.Features.Invoices;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Outbox;

/// <summary>
/// Storage-queue consumer for the <c>generate-invoice</c> queue per
/// ADR 0020 + T-0069. The queue message body is the outbox event id
/// (string); the function loads the row from Postgres (the system of
/// record), deserialises the <see cref="InvoiceGenerateOutboxPayload"/>,
/// and dispatches <see cref="IssueInvoice.Command"/> via Mediator.
///
/// <para>
/// <b>Throws on every failure path</b> per T-0069 locked decision 5 +
/// the ticket's "Why GenerateInvoiceFunction throws instead of returning
/// BusinessResult" note. Azure Functions queue triggers signal retry via
/// thrown exceptions — catching <see cref="IssueInvoice"/> failures and
/// returning normally would silently swallow them. The
/// <see cref="IssueInvoice.Handler"/> is idempotent (T-0068b AC-5), so
/// the throw-on-failure semantics are safe under at-least-once delivery.
/// </para>
///
/// <para>
/// <b>No Function-level ProcessedAt tracking</b> per locked decision 5
/// — <see cref="IssueInvoice.Handler"/>'s step-3 pre-check
/// (<c>GetByOrderIdAsync</c> short-circuit) handles duplicates. A second
/// invocation against the same outbox row finds the existing Invoice
/// and returns its values verbatim; no second number allocation, no
/// second render, no second blob upload.
/// </para>
/// </summary>
public sealed class GenerateInvoiceFunction(
    IOutboxConsumerRepository outbox,
    ISender mediator,
    ILogger<GenerateInvoiceFunction> logger)
{
    public const string FunctionName = "GenerateInvoice";

    [Function(FunctionName)]
    public async Task Run(
        [QueueTrigger("%OutboxQueues:GenerateInvoiceQueueName%", Connection = "AzureWebJobsStorage")]
        string outboxEventId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outboxEventId))
        {
            // Throw so Azure dead-letters the message after maxDequeueCount
            // attempts and an ops operator can inspect <generate-invoice>-poison.
            // Mirrors T-0029's SendEmailFunction poison-routing pattern.
            logger.LogError(
                "GenerateInvoice received empty queue message; failing for poison-queue routing.");
            throw new InvalidOperationException(
                "GenerateInvoice received an empty queue message; expected an OutboxEvent id.");
        }

        var evt = await outbox.GetByIdAsync(outboxEventId, cancellationToken);
        if (evt is null)
        {
            logger.LogError(
                "GenerateInvoice: OutboxEvent {OutboxEventId} not found. Throwing for queue retry.",
                outboxEventId);
            throw new InvalidOperationException(
                $"OutboxEvent {outboxEventId} not found.");
        }

        InvoiceGenerateOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InvoiceGenerateOutboxPayload>(evt.PayloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "GenerateInvoice: malformed InvoiceGenerateOutboxPayload for outbox {OutboxEventId}.",
                outboxEventId);
            throw new InvalidOperationException(
                $"Malformed InvoiceGenerateOutboxPayload for {outboxEventId}.", ex);
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.OrderId))
        {
            logger.LogError(
                "GenerateInvoice: InvoiceGenerateOutboxPayload for outbox {OutboxEventId} decoded null or missing OrderId.",
                outboxEventId);
            throw new InvalidOperationException(
                $"InvoiceGenerateOutboxPayload for {outboxEventId} is null or missing OrderId.");
        }

        var result = await mediator.Send(
            new IssueInvoice.Command(payload.OrderId), cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogError(
                "GenerateInvoice: IssueInvoice failed for outbox {OutboxEventId} (order {OrderId}): {Code} ({Type}).",
                outboxEventId, payload.OrderId, result.Error!.Code, result.Error.Type);
            // Re-throw so the queue retry policy fires. The IssueInvoice
            // handler is idempotent — repeated attempts that succeed find
            // the existing Invoice and short-circuit; repeated failures
            // burn the queue retry budget until poison-routing kicks in.
            throw new InvalidOperationException(
                $"IssueInvoice failed for outbox {outboxEventId} (order {payload.OrderId}): {result.Error.Code}");
        }

        logger.LogInformation(
            "GenerateInvoice: outbox {OutboxEventId} → invoice {InvoiceNumber} (id {InvoiceId}, blob {PdfBlobPath}).",
            outboxEventId, result.Value!.InvoiceNumber, result.Value.InvoiceId, result.Value.PdfBlobPath);
    }
}
