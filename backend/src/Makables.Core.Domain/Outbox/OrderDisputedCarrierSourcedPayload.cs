using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Shipping;

namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the carrier-sourced dispute event, enqueued by
/// <c>DisputeShipment.Handler</c> (T-0078 STUB) when the T-0078
/// status-sync Function observes a Packeta
/// <see cref="ShipmentState.Returned"/> or <see cref="ShipmentState.Failed"/>
/// terminal state.
///
/// <para>
/// <see cref="CarrierState"/> preserves the raw Packeta state in the
/// audit trail so T-0106 admin processing can distinguish Returned vs
/// Failed even after <see cref="DisputeReason"/> semantics evolve.
/// </para>
///
/// <para>
/// PascalCase JSON property names match the order-email payload
/// convention so a single <see cref="System.Text.Json.JsonSerializer"/>
/// with default options round-trips every outbox payload shape.
/// </para>
/// </summary>
public sealed record OrderDisputedCarrierSourcedPayload(
    string OrderId,
    DisputeReason Reason,
    ShipmentState CarrierState);
