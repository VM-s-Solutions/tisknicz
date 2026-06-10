namespace Makables.Core.Domain.Orders;

/// <summary>
/// Identifies which caller drove a <see cref="Order"/>
/// → <see cref="OrderState.Cancelled"/> transition. Stamped on
/// <see cref="Order.CancellationSource"/> at <see cref="Order.Cancel"/>
/// time so dispute trails + admin reporting can answer "how did this
/// order close?" without joining the outbox / audit logs.
///
/// <para>
/// Explicit <c>: short</c> backing mirrors
/// <see cref="OrderDeliverySource"/> — keeps the EF column at SMALLINT
/// (Int16 vs Int32 saves 2 bytes per row) and lets the EF enum-conversion
/// map without a default Int32 round-trip. Values are stable; new sources
/// (e.g. a future moderator role) append. T-0083 §C.
/// </para>
/// </summary>
public enum OrderCancellationSource : short
{
    /// <summary>
    /// Customer initiated the cancel via a customer-host endpoint (T-0105
    /// owns the action; T-0083 only introduces the enum value).
    /// </summary>
    Customer = 0,

    /// <summary>
    /// 24-hour pending-payment TTL elapsed without payment.
    /// <see cref="Features.Orders.CancelExpiredOrder"/> writes this via the
    /// T-0083 <c>CancelExpiredPendingPaymentOrdersFunction</c>.
    /// </summary>
    AutoExpiry = 1,

    /// <summary>
    /// Admin manually cancelled the order (T-0107 owns the action; T-0083
    /// only introduces the enum value).
    /// </summary>
    Admin = 2,
}
