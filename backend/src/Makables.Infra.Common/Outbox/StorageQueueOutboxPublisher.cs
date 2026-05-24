using Azure.Storage.Queues;
using Makables.Core.Domain.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Common.Outbox;

/// <summary>
/// Azure Storage Queue <see cref="IOutboxQueuePublisher"/> impl per ADR
/// 0020 §"Why a hybrid". The message body is the bare outbox event id —
/// the payload is NEVER copied into the queue, only into Postgres. The
/// queue consumer (<c>SendEmailFunction</c> in <c>Makables.Functions</c>)
/// re-reads the row from the outbox by id so the system of record is
/// always the database.
///
/// The <see cref="QueueClient"/> is constructed once per queue with
/// <see cref="QueueMessageEncoding.Base64"/> so messages round-trip
/// safely (Azure Functions queue triggers default to Base64 in v4).
/// Queue is auto-created on first publish (idempotent), keeping local
/// dev (Azurite) and a fresh prod account both working without manual
/// provisioning.
/// </summary>
public sealed class StorageQueueOutboxPublisher : IOutboxQueuePublisher
{
    private readonly QueueClient _sendEmailQueue;
    private readonly ILogger<StorageQueueOutboxPublisher> _logger;
    private bool _ensuredQueueExists;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public StorageQueueOutboxPublisher(
        IOptions<OutboxQueueOptions> options,
        ILogger<StorageQueueOutboxPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
            throw new InvalidOperationException("OutboxQueues:ConnectionString is not configured.");
        if (string.IsNullOrWhiteSpace(opts.SendEmailQueueName))
            throw new InvalidOperationException("OutboxQueues:SendEmailQueueName is not configured.");

        _sendEmailQueue = new QueueClient(
            opts.ConnectionString,
            opts.SendEmailQueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        _logger = logger;
    }

    public async Task PublishSendEmailAsync(string outboxEventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventId);
        await EnsureQueueAsync(cancellationToken);
        await _sendEmailQueue.SendMessageAsync(outboxEventId, cancellationToken);
        _logger.LogDebug("Published outbox event {OutboxEventId} to {QueueName}.",
            outboxEventId, _sendEmailQueue.Name);
    }

    private async Task EnsureQueueAsync(CancellationToken cancellationToken)
    {
        if (_ensuredQueueExists) return;
        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (_ensuredQueueExists) return;
            await _sendEmailQueue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _ensuredQueueExists = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}
