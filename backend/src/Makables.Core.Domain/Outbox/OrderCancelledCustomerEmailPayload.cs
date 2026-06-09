using Makables.Core.Domain.Orders;

namespace Makables.Core.Domain.Outbox;

/// <summary>
/// JSON payload for the customer-facing "Vaše objednávka byla zrušena
/// (platba neproběhla)" email, enqueued by
/// <see cref="Features.Orders.CancelExpiredOrder"/> after the
/// PendingPayment → Cancelled transition. T-0083 (US-customer-0010 AC-3).
///
/// <para>
/// <see cref="Reason"/> stamps the <see cref="OrderCancellationSource"/>
/// at enqueue time so the email-routing branch in
/// <c>EmailSendService</c> can pick the AutoExpiry-source copy variant.
/// T-0083 ships AutoExpiry copy only; Customer / Admin variants ride
/// extensions of this payload + template in T-0105 / T-0107.
/// </para>
///
/// <para>
/// PascalCase JSON property names match the
/// <see cref="OrderPaidCustomerEmailPayload"/> convention so a single
/// <see cref="System.Text.Json.JsonSerializer"/> with default options
/// round-trips every order-email payload shape.
/// </para>
/// </summary>
public sealed record OrderCancelledCustomerEmailPayload(
    string OrderId,
    string OrderNumber,
    string Email,
    string ContactName,
    OrderCancellationSource Reason,
    string LanguageCode,
    string ActionUrl);
