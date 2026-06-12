namespace Makables.Core.Domain.Orders;

/// <summary>
/// Identifies which caller drove the <see cref="OrderState.Shipped"/> →
/// <see cref="OrderState.Delivered"/> transition. Stamped on
/// <see cref="Order.DeliverySource"/> at <see cref="Order.MarkAsDelivered"/>
/// time so dispute trails (Phase 5 T-0106 / T-0118) and analytics can
/// answer "how did this order close?" without joining outbox / audit logs.
///
/// <para>
/// Explicit <c>: short</c> backing keeps the EF column at <c>SMALLINT</c>
/// (Int16 vs Int32 saves 2 bytes per row) and lets the EF enum-conversion
/// map without a default-Int32 round-trip. Values are stable; new sources
/// (e.g. <c>AdminManual = 3</c>) append. T-0076 locked decision A.1.
/// </para>
/// </summary>
public enum OrderDeliverySource : short
{
    /// <summary>
    /// Customer pressed "Označit jako doručeno" on the customer-host
    /// order-detail page. Dispatched by T-0076's
    /// <c>POST /api/v1/customer/orders/{orderId}/deliver</c> endpoint.
    /// </summary>
    Customer = 0,

    /// <summary>
    /// 7-day auto-deliver timer expired without an explicit confirmation.
    /// Dispatched by T-0077 <c>AutoDeliverOrdersFunction</c>.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Packeta carrier-status sync reported the package as delivered.
    /// Dispatched by T-0078 <c>SyncShipmentStatusesFunction</c> with the
    /// carrier's authoritative <c>DeliveredAt</c> timestamp when present.
    /// </summary>
    Carrier = 2,

    /// <summary>
    /// Admin manually marked the order delivered via T-0107
    /// <c>ChangeOrderStateManually</c> (carrier-blind delivery — the
    /// customer confirmed receipt out-of-band but neither the customer
    /// endpoint nor the carrier sync fired). The audit log carries the
    /// admin's mandatory reason.
    /// </summary>
    AdminManual = 3,
}
