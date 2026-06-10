namespace Makables.Core.Domain.Outbox;

/// <summary>
/// Symmetric maker-facing "new message" digest payload, enqueued by
/// <see cref="Features.OrderMessages.PostCustomerOrderMessage"/> when a
/// customer posts AND the maker's 5-min debounce window has elapsed
/// (T-0079 §C.7).
///
/// <para>
/// <see cref="SenderName"/> here is the customer's snapshot contact name
/// (the maker sees a name, never the customer's account-registered
/// email — T-0081 §A.2 GDPR data-minimization lock).
/// </para>
/// </summary>
public sealed record OrderMessagePostedMakerEmailPayload(
    string OrderId,
    string OrderNumber,
    string MakerEmail,
    string SenderName,
    int UnreadMessageCount,
    string LanguageCode,
    string ActionUrl);
