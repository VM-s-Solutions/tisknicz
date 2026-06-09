namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "Nová zpráva k objednávce
/// {orderNumber}" digest email, enqueued by
/// <see cref="Features.OrderMessages.PostMakerOrderMessage"/> when a
/// maker posts AND the customer's 5-min debounce window has elapsed
/// (T-0079 §C.7). Single template covers the digest — the customer
/// opens the order detail page to read the new messages.
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// round-trips every order-email payload shape.
/// </para>
///
/// <para>
/// <see cref="UnreadMessageCount"/> is captured at enqueue time —
/// snapshot rather than re-computed, so the email body reflects the
/// state at trigger moment ("you have N unread messages"). If the user
/// reads more before the email arrives, the email will overstate;
/// acceptable for a digest. <see cref="SenderName"/> is the maker's
/// company name (the customer sees who messaged them).
/// </para>
/// </summary>
public sealed record OrderMessagePostedCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    string SenderName,
    int UnreadMessageCount,
    string LanguageCode,
    string ActionUrl);
