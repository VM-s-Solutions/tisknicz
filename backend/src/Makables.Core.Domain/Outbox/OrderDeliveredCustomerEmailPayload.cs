namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "your order has been delivered"
/// email, enqueued by <c>MarkOrderDelivered.Handler</c> (T-0076) after
/// the Shipped → Delivered transition. Single email per delivery; no
/// maker counterpart per T-0076 locked decision A.2.
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// round-trips every order-email payload shape.
/// </para>
///
/// <para>
/// <see cref="LanguageCode"/> is resolved at enqueue time per T-0028's
/// <c>ILanguageResolver</c> pattern (User.PreferredLanguage →
/// CountryConfiguration.DefaultLanguageCode → fallback). Captured here so
/// the consumer (<c>EmailSendService</c>) doesn't re-resolve.
/// </para>
///
/// <para>
/// <see cref="ActionUrl"/> is pre-baked by the producer:
/// <c>{WebBaseUrl}/objednavka/{OrderId}</c>. <b>No invoice attachment</b>
/// — the invoice ships only on <see cref="OrderPaidCustomerEmailPayload"/>
/// per T-0069 locked decision 10; subsequent state-change emails are
/// reference-only.
/// </para>
/// </summary>
public sealed record OrderDeliveredCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    string LanguageCode,
    string ActionUrl);
