namespace Makables.Core.Domain.OrderMessages;

/// <summary>
/// Identifies which party authored an <see cref="OrderMessage"/>. Stored
/// as a small explicit-valued enum on the message row; the matching
/// recipient is the opposite role (sender is never their own recipient).
///
/// <para>
/// Explicit numeric values are stable wire codes per project convention
/// (mirrors <see cref="Orders.OrderDeliverySource"/> /
/// <see cref="Orders.OrderCancellationSource"/>). Stored as <c>short</c>
/// on the column so two new roles (e.g. <c>Admin = 3</c> moderator
/// post) can append without an enum widening. T-0079 §C.2.
/// </para>
/// </summary>
public enum OrderMessageAuthorRole : short
{
    /// <summary>The order's customer posted the message.</summary>
    Customer = 1,

    /// <summary>The order's maker posted the message.</summary>
    Maker = 2,
}
