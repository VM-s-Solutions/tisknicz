namespace Makables.Core.Domain.Orders;

/// <summary>
/// How the admin closed a dispute (T-0106 §C.3). The outcome drives the
/// post-restore edge in <c>ResolveDispute.Handler</c>:
/// <see cref="Refunded"/> dispatches <c>RefundOrder.Command</c> for the
/// full remaining amount; <see cref="Cancelled"/> calls
/// <c>Order.Cancel(clock, Admin)</c> (only reachable when the restored
/// state is Paid/Accepted); <see cref="Resumed"/> means the order
/// proceeds from the restored state as if undisputed.
///
/// <para>
/// Explicit <c>: short</c> backing — SMALLINT column, stable wire
/// values, new outcomes append.
/// </para>
/// </summary>
public enum DisputeResolutionOutcome : short
{
    Refunded = 0,
    Resumed = 1,
    Cancelled = 2,
}
