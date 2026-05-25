namespace Makables.Core.AppServices.Common;

/// <summary>
/// Tunables for the T-0029 outbox sweep. Bound from <c>OutboxDispatcher</c>
/// in configuration. Lives in <c>Core.AppServices</c> (not <c>Infra.Common</c>)
/// because the sweep semantics are an application-services concern; queue
/// transport config (connection string, queue name) lives separately in
/// <c>OutboxQueueOptions</c>.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// How long after publishing a row to the queue we hide it from the
    /// next outbox sweep. Tunable per env so dev can be more aggressive
    /// (1 min) while prod stays conservative (15 min). If the consumer
    /// crashes or the queue silently drops the message, the row becomes
    /// eligible for re-publish after this window. T-0029 CQ reviewer m-1.
    /// </summary>
    public int HandoffParkMinutes { get; set; } = 15;
}
