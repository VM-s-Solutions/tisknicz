namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Fan-out queue abstraction for T-0029's <c>ProcessOutboxFunction</c>.
/// Per ADR 0020 §"Why a hybrid": the outbox table is the system of
/// record; the queues are the transport for parallelism. ProcessOutbox
/// reads the outbox and publishes the row id to the per-handler queue;
/// the specialised Function (e.g. <c>SendEmailFunction</c>) consumes
/// the queue, loads the row, does the work, and updates the row's
/// status.
///
/// Only the id flows through the queue — the payload always stays in
/// Postgres so the queue can't be a source of truth or a place where
/// PII lingers. The queue message is opaque except for the id.
/// </summary>
public interface IOutboxQueuePublisher
{
    /// <summary>
    /// Publish an "process this outbox event" message to the
    /// <c>send-email</c> queue so a <c>SendEmailFunction</c> picks it up.
    /// </summary>
    Task PublishSendEmailAsync(string outboxEventId, CancellationToken cancellationToken);

    /// <summary>
    /// Publish an "process this outbox event" message to the
    /// <c>generate-invoice</c> queue so a <c>GenerateInvoiceFunction</c>
    /// picks it up and dispatches <c>IssueInvoice.Command</c> via
    /// Mediator. Per T-0069 locked decision 2: separate queue, separate
    /// retry budget, separate poison-message dead-letter — render/upload
    /// failures cannot contaminate the email-send retry budget.
    /// </summary>
    Task PublishGenerateInvoiceAsync(string outboxEventId, CancellationToken cancellationToken);
}
