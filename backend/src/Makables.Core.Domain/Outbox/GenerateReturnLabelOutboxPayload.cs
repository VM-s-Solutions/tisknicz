namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the <c>shipping.generate.returnLabel</c> outbox event
/// (T-0146), enqueued by admin's <c>GenerateReturnLabel.Handler</c>.
/// Consumer is the shared <c>GenerateLabelFunction</c> (T-0074) — it
/// discriminates on <c>OutboxEvent.EventType</c> and dispatches
/// <c>FetchAndStoreReturnLabel.Command</c> for this payload shape.
///
/// <para>
/// Mirrors <see cref="GenerateLabelOutboxPayload"/>'s minimalism —
/// <c>FetchAndStoreReturnLabel.Handler</c> re-loads the
/// <see cref="Orders.Dispute"/> fresh via
/// <c>IDisputeRepository.GetByIdUnscopedAsync</c> and reads
/// <c>ReturnCarrierRef</c> at execution time.
/// </para>
/// </summary>
public sealed record GenerateReturnLabelOutboxPayload(string DisputeId);
