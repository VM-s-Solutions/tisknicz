namespace Makables.Core.Domain.Orders.Sorting;

/// <summary>
/// Sort key for the customer + maker paged order list reads (T-0080
/// <c>GetCustomerOrders</c>, T-0081 <c>GetMakerOrders</c>). Explicit
/// numeric values are stable wire codes — new sort modes append rather
/// than reorder.
///
/// <para>
/// The fixed list keeps the public surface small at MVP per the bundle
/// locked decisions (§C). Adding a new sort key is additive (new
/// integer); reordering an existing one would silently rewrite every
/// historical query-string. Mirrors <see cref="Orders.OrderState"/>'s
/// explicit-int policy.
/// </para>
/// </summary>
public enum OrderSort
{
    /// <summary>Default — newest first. Implementation tie-breaks on Id desc for stable pagination.</summary>
    CreatedAtDesc = 0,

    /// <summary>Oldest first.</summary>
    CreatedAtAsc = 1,

    /// <summary>Largest total first (customer "what did I spend the most on").</summary>
    TotalAmountDesc = 2,

    /// <summary>Smallest total first.</summary>
    TotalAmountAsc = 3,

    /// <summary>Group by state alphabetically. Useful when scanning for a particular bucket.</summary>
    StateAsc = 4,
}
