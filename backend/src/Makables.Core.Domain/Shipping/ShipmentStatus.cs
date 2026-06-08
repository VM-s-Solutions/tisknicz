namespace Makables.Core.Domain.Shipping;

/// <summary>
/// Result of <see cref="IShippingCarrier.GetStatusAsync"/> — the current
/// <see cref="ShipmentState"/> + an optional carrier-reported delivery
/// timestamp. T-0078 polls this and converts a Delivered signal into
/// an <c>Order.MarkAsDelivered</c> call when it arrives ahead of
/// <c>Order.AutoDeliverAt</c>.
/// </summary>
public sealed record ShipmentStatus(ShipmentState State, DateTimeOffset? DeliveredAt);
