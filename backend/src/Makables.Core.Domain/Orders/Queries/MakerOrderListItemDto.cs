namespace Makables.Core.Domain.Orders.Queries;

/// <summary>
/// One row in the maker dashboard order list (T-0081). Flat shape;
/// distinct from <see cref="CustomerOrderListItemDto"/> — the maker
/// surface emphasises the maker's net payout (<see cref="MakerPayoutAmountMinor"/>)
/// instead of the platform fee, and surfaces the customer's contact
/// NAME (not email — T-0081 GDPR data-minimization lock A.2).
///
/// <para>
/// <b>Customer email is deliberately absent from this DTO.</b> The
/// projection's expression tree does not even name <c>ContactEmail</c>
/// — a grep-friendly absence so a future SELECT-* refactor cannot
/// accidentally leak it. Maker-customer communication routes through
/// the T-0079 message thread.
/// </para>
///
/// <para>
/// <see cref="UnreadMessageCount"/> is reserved for T-0079; populated as
/// null until that ticket ships. The field appears in the wire contract
/// today so T-0079 is a pure projection-logic edit with zero NSwag /
/// frontend churn.
/// </para>
/// </summary>
public sealed record MakerOrderListItemDto(
    string OrderId,
    string OrderNumber,
    OrderState State,
    long TotalAmountMinor,
    long MakerPayoutAmountMinor,
    string Currency,
    DateTimeOffset CreatedAt,
    string CustomerContactName,
    ShippingMethod ShippingMethod,
    string? ProductTitle,
    int? UnreadMessageCount);
