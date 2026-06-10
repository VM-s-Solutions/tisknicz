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
/// <see cref="UnreadMessageCount"/> reads the denormalized
/// <c>orders.maker_unread_message_count</c> column verbatim since T-0079
/// (0 for orders with no unread messages). The type stays <c>int?</c>
/// because the T-0081 wire contract shipped nullable; tightening it is
/// NSwag churn for zero gain.
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
