namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Full Packeta state space mapped onto a stable enum the platform
/// reasons about. T-0078 (status sync) translates Packeta-specific
/// labels (e.g. <c>"received data"</c>, <c>"in branch"</c>,
/// <c>"delivered"</c>) onto these values. Per ADR 0017 + T-0070
/// locked decision B.
///
/// <para>
/// The Order aggregate's own <see cref="Orders.OrderState"/> machine is
/// driven by maker actions + auto-deliver timers — NOT by this enum.
/// This enum is the carrier-side report card; T-0078's poller compares
/// it to <see cref="Orders.OrderState"/> and transitions Shipped → Delivered
/// when a Delivered signal arrives ahead of <c>Order.AutoDeliverAt</c>.
/// </para>
/// </summary>
public enum ShipmentState
{
    /// <summary>Packet created at the carrier; no transit yet.</summary>
    Created = 0,

    /// <summary>Packet in transit (received by carrier, en route to pickup branch).</summary>
    InTransit = 1,

    /// <summary>Customer has picked up the package — terminal.</summary>
    Delivered = 2,

    /// <summary>Customer never picked up; package returned to maker — terminal.</summary>
    Returned = 3,

    /// <summary>Carrier flagged a failure (lost / damaged / refused) — terminal.</summary>
    Failed = 4,
}
