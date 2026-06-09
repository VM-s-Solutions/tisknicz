namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// Filter envelope for the customer + maker paged order list reads
/// (T-0080 <c>GetCustomerOrders</c>, T-0081 <c>GetMakerOrders</c>). Every
/// field is nullable — the caller passes whichever subset the request
/// query-string carried; the EF projection applies each non-null clause
/// conditionally.
///
/// <para>
/// <see cref="DateRangeStart"/> compares <c>&gt;=</c> against
/// <see cref="Common.Auditable.CreatedAt"/>; <see cref="DateRangeEnd"/>
/// compares <c>&lt;=</c>. Both bounds are inclusive — matches AC-3 on
/// both list tickets.
/// </para>
/// </summary>
public sealed record OrderFilter(
    OrderState? State,
    DateTimeOffset? DateRangeStart,
    DateTimeOffset? DateRangeEnd);
