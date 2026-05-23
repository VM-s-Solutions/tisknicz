using Makables.Core.Domain.Common;
using Makables.Core.Domain.Outbox;

namespace Makables.Infra.Database.Outbox;

/// <summary>
/// Producer-side <see cref="IOutbox"/> impl. Calls <c>DbSet.Add</c> on the
/// new <see cref="OutboxEvent"/> within the surrounding command's
/// transaction; UnitOfWorkPipelineBehavior commits it atomically with the
/// state change. Per ADR 0014 / patterns §A.20.
/// </summary>
public sealed class OutboxWriter(
    MakablesDbContext db,
    IIdGenerator idGenerator,
    IClock clock)
    : IOutbox
{
    public void Enqueue(string aggregateId, string eventType, string payloadJson)
    {
        var evt = OutboxEvent.Enqueue(
            id: idGenerator.Next(),
            aggregateId: aggregateId,
            eventType: eventType,
            payloadJson: payloadJson,
            now: clock.UtcNow);

        db.Set<OutboxEvent>().Add(evt);
    }
}
