namespace Makables.Core.Domain.Orders;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Per role/order.md and
/// ADR 0002 the canonical edges are:
///
/// <list type="bullet">
///   <item><description><see cref="PendingPayment"/> → <see cref="Paid"/> (Comgate webhook)</description></item>
///   <item><description><see cref="Paid"/> → <see cref="Accepted"/> (maker confirms)</description></item>
///   <item><description><see cref="Accepted"/> → <see cref="Shipped"/> (maker dispatches)</description></item>
///   <item><description><see cref="Shipped"/> → <see cref="Delivered"/> (customer / carrier / auto)</description></item>
///   <item><description><see cref="Delivered"/> → <see cref="Completed"/> (payout settled)</description></item>
///   <item><description>Multiple → <see cref="Cancelled"/> / <see cref="Refunded"/> / <see cref="Disputed"/></description></item>
/// </list>
///
/// <para>
/// The enum values are explicit because the column is stored as the
/// string name via <c>HasConversion&lt;string&gt;()</c> (mirroring
/// <c>PriceType</c>) — but if a future EF caller bypasses the converter
/// the explicit ints pin the wire-to-storage mapping and stop a reorder
/// from silently rewriting persisted rows. Wire shape across the API is
/// the string name (<c>JsonStringEnumConverter</c> from T-0049b).
/// </para>
/// </summary>
public enum OrderState
{
    /// <summary>Order created; payment session opened but not yet confirmed.</summary>
    PendingPayment = 0,

    /// <summary>Comgate confirmed the payment; maker now has the ball.</summary>
    Paid = 1,

    /// <summary>Maker confirmed they will fulfil the order.</summary>
    Accepted = 2,

    /// <summary>Maker dispatched the order; auto-deliver timer is running.</summary>
    Shipped = 3,

    /// <summary>Order received by the customer (manual, auto, or carrier-confirmed).</summary>
    Delivered = 4,

    /// <summary>Maker payout settled; lifecycle is closed.</summary>
    Completed = 5,

    /// <summary>Order cancelled before delivery (see role/order.md for who-can-when).</summary>
    Cancelled = 6,

    /// <summary>Admin reversed the charge after payment cleared.</summary>
    Refunded = 7,

    /// <summary>Customer or maker opened a dispute; awaits admin adjudication.</summary>
    Disputed = 8,
}
