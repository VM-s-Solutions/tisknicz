namespace Makables.Core.Domain.Orders;

/// <summary>
/// Who opened the dispute. <b>Always set server-side by the handler</b>
/// — never client-supplied (the customer endpoint stamps
/// <see cref="Customer"/>, the maker endpoint <see cref="Maker"/>, the
/// T-0078 carrier sweep <see cref="Carrier"/>, the admin endpoint
/// <see cref="Admin"/>). T-0106 user decision Q2.
///
/// <para>
/// Explicit <c>: short</c> backing per the
/// <see cref="OrderCancellationSource"/> precedent — SMALLINT column,
/// stable wire values, new sources append.
/// </para>
/// </summary>
public enum DisputeSource : short
{
    Customer = 0,
    Maker = 1,
    Carrier = 2,
    Admin = 3,
}
