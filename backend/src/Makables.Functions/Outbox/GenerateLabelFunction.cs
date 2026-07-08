using System.Text.Json;
using Makables.Core.AppServices.Features.Shipping;
using Makables.Core.Domain.Outbox;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Makables.Functions.Outbox;

/// <summary>
/// Storage-queue consumer for the <c>generate-label</c> queue per
/// ADR 0020 + T-0074. The queue message body is the outbox event id
/// (string); the function loads the row from Postgres (the system of
/// record) and discriminates on <c>OutboxEvent.EventType</c> (T-0146):
/// <see cref="OutboxEventTypes.ShippingGenerateLabel"/> deserialises
/// <see cref="GenerateLabelOutboxPayload"/> and dispatches
/// <see cref="FetchAndStoreShippingLabel.Command"/>;
/// <see cref="OutboxEventTypes.ShippingGenerateReturnLabel"/> deserialises
/// <see cref="Core.Domain.Outbox.GenerateReturnLabelOutboxPayload"/> and
/// dispatches <see cref="FetchAndStoreReturnLabel.Command"/> — same
/// class of work (Packeta label PDF fetch + blob cache-fill), reusing
/// the queue + retry budget rather than standing up a second one.
///
/// <para>
/// <b>Throws on every failure path</b> per T-0074 locked decision B +
/// the T-0069 GenerateInvoiceFunction precedent. Azure Functions queue
/// triggers signal retry via thrown exceptions — catching failures and
/// returning normally would silently swallow them. Both handlers are
/// idempotent (HEAD-check), so throw-on-failure semantics are safe under
/// at-least-once delivery.
/// </para>
/// </summary>
public sealed class GenerateLabelFunction(
    IOutboxConsumerRepository outbox,
    ISender mediator,
    ILogger<GenerateLabelFunction> logger)
{
    public const string FunctionName = "GenerateLabel";

    [Function(FunctionName)]
    public async Task Run(
        [QueueTrigger("%OutboxQueues:GenerateLabelQueueName%", Connection = "AzureWebJobsStorage")]
        string outboxEventId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outboxEventId))
        {
            logger.LogError(
                "GenerateLabel received empty queue message; failing for poison-queue routing.");
            throw new InvalidOperationException(
                "GenerateLabel received an empty queue message; expected an OutboxEvent id.");
        }

        var evt = await outbox.GetByIdAsync(outboxEventId, cancellationToken);
        if (evt is null)
        {
            logger.LogError(
                "GenerateLabel: OutboxEvent {OutboxEventId} not found. Throwing for queue retry.",
                outboxEventId);
            throw new InvalidOperationException(
                $"OutboxEvent {outboxEventId} not found.");
        }

        if (evt.EventType == OutboxEventTypes.ShippingGenerateReturnLabel)
        {
            await RunReturnLabelAsync(outboxEventId, evt.PayloadJson, cancellationToken);
            return;
        }

        await RunForwardLabelAsync(outboxEventId, evt.PayloadJson, cancellationToken);
    }

    private async Task RunForwardLabelAsync(
        string outboxEventId, string payloadJson, CancellationToken cancellationToken)
    {
        GenerateLabelOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GenerateLabelOutboxPayload>(payloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "GenerateLabel: malformed GenerateLabelOutboxPayload for outbox {OutboxEventId}.",
                outboxEventId);
            throw new InvalidOperationException(
                $"Malformed GenerateLabelOutboxPayload for {outboxEventId}.", ex);
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.OrderId))
        {
            logger.LogError(
                "GenerateLabel: GenerateLabelOutboxPayload for outbox {OutboxEventId} decoded null or missing OrderId.",
                outboxEventId);
            throw new InvalidOperationException(
                $"GenerateLabelOutboxPayload for {outboxEventId} is null or missing OrderId.");
        }

        var result = await mediator.Send(
            new FetchAndStoreShippingLabel.Command(payload.OrderId), cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogError(
                "GenerateLabel: FetchAndStoreShippingLabel failed for outbox {OutboxEventId} (order {OrderId}): {Code} ({Type}).",
                outboxEventId, payload.OrderId, result.Error!.Code, result.Error.Type);
            throw new InvalidOperationException(
                $"FetchAndStoreShippingLabel failed for outbox {outboxEventId} (order {payload.OrderId}): {result.Error.Code}");
        }

        logger.LogInformation(
            "GenerateLabel: outbox {OutboxEventId} → label stored at {BlobPath}.",
            outboxEventId, result.Value!.BlobPath);
    }

    private async Task RunReturnLabelAsync(
        string outboxEventId, string payloadJson, CancellationToken cancellationToken)
    {
        GenerateReturnLabelOutboxPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GenerateReturnLabelOutboxPayload>(payloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "GenerateLabel: malformed GenerateReturnLabelOutboxPayload for outbox {OutboxEventId}.",
                outboxEventId);
            throw new InvalidOperationException(
                $"Malformed GenerateReturnLabelOutboxPayload for {outboxEventId}.", ex);
        }
        if (payload is null || string.IsNullOrWhiteSpace(payload.DisputeId))
        {
            logger.LogError(
                "GenerateLabel: GenerateReturnLabelOutboxPayload for outbox {OutboxEventId} decoded null or missing DisputeId.",
                outboxEventId);
            throw new InvalidOperationException(
                $"GenerateReturnLabelOutboxPayload for {outboxEventId} is null or missing DisputeId.");
        }

        var result = await mediator.Send(
            new FetchAndStoreReturnLabel.Command(payload.DisputeId), cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogError(
                "GenerateLabel: FetchAndStoreReturnLabel failed for outbox {OutboxEventId} (dispute {DisputeId}): {Code} ({Type}).",
                outboxEventId, payload.DisputeId, result.Error!.Code, result.Error.Type);
            throw new InvalidOperationException(
                $"FetchAndStoreReturnLabel failed for outbox {outboxEventId} (dispute {payload.DisputeId}): {result.Error.Code}");
        }

        logger.LogInformation(
            "GenerateLabel: outbox {OutboxEventId} → return label stored at {BlobPath}.",
            outboxEventId, result.Value!.BlobPath);
    }
}
