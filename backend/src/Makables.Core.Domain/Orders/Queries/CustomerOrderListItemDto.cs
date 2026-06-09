namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// One row in the customer dashboard order list (T-0080). Flat shape with
/// denormalized <see cref="MakerName"/> + first <see cref="ProductTitle"/>
/// projected inline per locked decision A.4 — single SQL round-trip,
/// renderable in a Server Component without per-row follow-up queries.
///
/// <para>
/// <see cref="ProductTitle"/> is nullable for custom orders (no
/// <c>ProductId</c> link); the frontend renders a "Vlastní zakázka"
/// label client-side per US-customer-0016.
/// </para>
///
/// <para>
/// Lives in <c>Core.Domain</c> alongside <see cref="IOrderQueries"/> per
/// the T-0049a precedent (<c>MakerProductListItem</c> ships in the same
/// domain folder as <c>IMakerProductQueries</c>). The DTO carries no
/// domain behaviour — it's a read-shape.
/// </para>
/// </summary>
public sealed record CustomerOrderListItemDto(
    string OrderId,
    string OrderNumber,
    OrderState State,
    long TotalAmountMinor,
    string Currency,
    DateTimeOffset CreatedAt,
    string MakerName,
    string? ProductTitle);
