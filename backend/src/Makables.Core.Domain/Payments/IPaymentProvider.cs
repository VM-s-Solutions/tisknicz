using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace Makables.Core.Domain.Payments;

/// <summary>
/// Adapter for an external payment gateway per ADR 0016. One implementation
/// per provider (Comgate at MVP; future Stripe / Adyen). Selected per country
/// via <see cref="IPaymentProviderFactory.ResolveAsync"/> reading
/// <c>CountryConfiguration.DefaultPaymentProvider</c>.
///
/// <para>
/// <b>Invariants</b> (role/payment-provider.md):
/// <list type="bullet">
///   <item><description>Webhook verification always re-fetches status from the gateway
///     authoritatively — the body alone is never trusted.</description></item>
///   <item><description>The adapter never mutates the order. State transitions
///     happen via Mediator commands in the application layer.</description></item>
///   <item><description>The adapter never writes to the database.</description></item>
/// </list>
/// </para>
///
/// <para>
/// All four methods return <see cref="BusinessResult{T}"/>. Exceptions from
/// the underlying HTTP client are translated to <see cref="ErrorType.Transient"/>
/// (network blip, timeout, 5xx, 408, 429) or <see cref="ErrorType.Permanent"/>
/// (provider rejected the request with a business error) or
/// <see cref="ErrorType.Configuration"/> (bad merchant id / secret —
/// alert ops) per the table in ADR 0016 §"Error mapping".
/// </para>
/// </summary>
public interface IPaymentProvider
{
    /// <summary>The provider code, e.g. <c>"comgate"</c>. Matches
    /// <c>CountryConfiguration.DefaultPaymentProvider</c> for selection.</summary>
    string Code { get; }

    /// <summary>
    /// T-0065. Create a payment session at the provider; return the redirect
    /// URL the customer's browser must navigate to + the provider's session
    /// reference (e.g. Comgate <c>transId</c>).
    /// </summary>
    Task<BusinessResult<PaymentSession>> CreatePaymentAsync(
        Order order,
        CancellationToken cancellationToken);

    /// <summary>
    /// T-0065. Verify the current status of an existing payment session
    /// at the provider. Used by the verify-then-recreate retry path
    /// (<c>CreatePaymentSession.Handler</c>) and by the webhook handler
    /// (T-0066) which never trusts the webhook body alone.
    /// </summary>
    Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(
        string providerRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// T-0066. Parse + verify an inbound webhook from the provider.
    /// Implementations MUST re-fetch the authoritative status from the
    /// gateway before returning success — the body alone is untrusted.
    /// <b>NOT IMPLEMENTED at T-0065</b>: implementations throw
    /// <see cref="NotSupportedException"/> until T-0066 ships.
    /// </summary>
    Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// T-0105. Refund a previously-captured payment. Amount is in minor
    /// currency units; pass the captured total for a full refund or a
    /// smaller value for a partial refund (provider permitting).
    /// <b>NOT IMPLEMENTED at T-0065</b>: implementations throw
    /// <see cref="NotSupportedException"/> until T-0105 ships.
    /// </summary>
    Task<BusinessResult<RefundReceipt>> RefundAsync(
        string providerRef,
        long amountMinor,
        string currency,
        CancellationToken cancellationToken);
}
