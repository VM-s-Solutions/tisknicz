namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Producer-side abstraction over the outbox. Handlers that need to fan
/// out side effects (email send, invoice render, label fetch) enqueue
/// events here inside the current command's transaction; the
/// UnitOfWorkPipelineBehavior commits everything atomically. Per ADR
/// 0014 / patterns §A.20.
///
/// Consumer side is the <c>ProcessOutboxFunction</c> in
/// <c>Makables.Functions</c> (added in T-0029).
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueue an event for the next outbox-processor sweep.
    /// Payload should be JSON-serialized by the caller; the outbox
    /// doesn't dictate serialization format.
    /// </summary>
    void Enqueue(string aggregateId, string eventType, string payloadJson);
}
