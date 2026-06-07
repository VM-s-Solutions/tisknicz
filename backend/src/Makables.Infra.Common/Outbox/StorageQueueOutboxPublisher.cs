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
    private readonly QueueClient _generateInvoiceQueue;
    private readonly ILogger<StorageQueueOutboxPublisher> _logger;

    // Per-queue ensure-once state. Independent semaphores so a Storage
    // outage that blocks one queue's CreateIfNotExistsAsync doesn't stall
    // the other queue's first publish.
    private bool _ensuredSendEmailQueueExists;
    private readonly SemaphoreSlim _ensureSendEmailLock = new(1, 1);
    private bool _ensuredGenerateInvoiceQueueExists;
    private readonly SemaphoreSlim _ensureGenerateInvoiceLock = new(1, 1);

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
        if (string.IsNullOrWhiteSpace(opts.GenerateInvoiceQueueName))
            throw new InvalidOperationException("OutboxQueues:GenerateInvoiceQueueName is not configured.");

        var clientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        _sendEmailQueue = new QueueClient(opts.ConnectionString, opts.SendEmailQueueName, clientOptions);
        _generateInvoiceQueue = new QueueClient(opts.ConnectionString, opts.GenerateInvoiceQueueName, clientOptions);
        _logger = logger;
    }

    public async Task PublishSendEmailAsync(string outboxEventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventId);
        await EnsureQueueAsync(
            _sendEmailQueue, _ensureSendEmailLock,
            () => _ensuredSendEmailQueueExists,
            () => _ensuredSendEmailQueueExists = true,
            cancellationToken);
        await _sendEmailQueue.SendMessageAsync(outboxEventId, cancellationToken);
        _logger.LogDebug("Published outbox event {OutboxEventId} to {QueueName}.",
            outboxEventId, _sendEmailQueue.Name);
    }

    public async Task PublishGenerateInvoiceAsync(string outboxEventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxEventId);
        await EnsureQueueAsync(
            _generateInvoiceQueue, _ensureGenerateInvoiceLock,
            () => _ensuredGenerateInvoiceQueueExists,
            () => _ensuredGenerateInvoiceQueueExists = true,
            cancellationToken);
        await _generateInvoiceQueue.SendMessageAsync(outboxEventId, cancellationToken);
        _logger.LogDebug("Published outbox event {OutboxEventId} to {QueueName}.",
            outboxEventId, _generateInvoiceQueue.Name);
    }

    private static async Task EnsureQueueAsync(
        QueueClient queue,
        SemaphoreSlim ensureLock,
        Func<bool> isEnsured,
        Action markEnsured,
        CancellationToken cancellationToken)
    {
        if (isEnsured()) return;
        await ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (isEnsured()) return;
            try
            {
                await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            finally
            {
                // Mark the attempt as taken even if CreateIfNotExistsAsync
                // threw (T-0029 sec reviewer M-7). Without this, every failed
                // sweep during a Storage outage hits the control plane in
                // addition to the publish call, doubling the API pressure.
                // The subsequent SendMessageAsync surfaces the real error;
                // if the queue doesn't exist on retry it will fail loudly
                // there too.
                markEnsured();
            }
        }
        finally
        {
            ensureLock.Release();
        }
    }
}
