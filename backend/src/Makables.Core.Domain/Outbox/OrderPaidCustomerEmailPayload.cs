namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "order received" confirmation
/// email, enqueued by <c>MarkOrderPaid.Handler</c> after the Comgate
/// webhook transitions the order to <c>OrderState.Paid</c>. T-0067
/// (US-customer-0010 AC-4).
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OneTimeTokenOutboxPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// (no <c>PropertyNamingPolicy</c>) round-trips both shapes.
/// </para>
///
/// <para>
/// <see cref="LanguageCode"/> is resolved at enqueue time per T-0028's
/// <c>ILanguageResolver</c> pattern (User.PreferredLanguage →
/// CountryConfiguration.DefaultLanguageCode → fallback). Captured here so
/// the consumer (<c>EmailSendService</c>) doesn't re-resolve — the
/// customer receives the email in whatever language they preferred at
/// payment time even if they edit their profile in the gap.
/// </para>
///
/// <para>
/// <see cref="ActionUrl"/> is pre-baked by the producer per T-0067
/// decision Q4: <c>{WebBaseUrl}/objednavka/{OrderId}</c>. The consumer
/// passes the URL verbatim to SendGrid as a Dynamic Template
/// substitution variable. Pre-baking locks the URL to the WebBaseUrl
/// in effect at-time-of-payment — config drift mid-flight does NOT
/// retroactively rewrite enqueued emails.
/// </para>
/// </summary>
public sealed record OrderPaidCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    long TotalAmountMinor,
    string Currency,
    string LanguageCode,
    string ActionUrl);
