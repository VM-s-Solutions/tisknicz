using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Infra.Clients.Dev;

/// <summary>
/// Non-production <see cref="IPaymentProvider"/> that stands in for the
/// real gateway so a tester can walk the whole checkout flow without
/// Comgate credentials, a card, or a public webhook endpoint.
///
/// <para>
/// <b>What it replaces.</b> <see cref="CreatePaymentAsync"/> mints a
/// synthetic session reference and returns a redirect URL that points
/// straight back at our own Customer host instead of a hosted payment
/// page. Navigating it marks the order paid and bounces the browser to
/// the order detail page, so "Zaplatit" behaves as a one-click pay.
/// </para>
///
/// <para>
/// <b>What it does NOT replace.</b> The state transition still runs
/// through <c>MarkOrderPaid</c> via Mediator (see
/// <c>DevPaymentsController</c> on the Customer host), so the outbox
/// emails, the invoice-generate event, the audit trail and every
/// aggregate invariant fire exactly as they do behind Comgate. This
/// adapter never touches the database — same invariant as every other
/// provider adapter per ADR 0016.
/// </para>
///
/// <para>
/// <b>Recognisable refs.</b> Every reference it issues carries the
/// <see cref="ProviderRefPrefix"/>. The confirm endpoint refuses any
/// order whose <c>PaymentProviderRef</c> lacks it, so the bypass can
/// never settle a real Comgate session even if the flag were switched on
/// against a database holding real orders.
/// </para>
/// </summary>
public sealed class DevPaymentProvider(
    IOptions<DevPaymentOptions> options,
    IClock clock,
    ILogger<DevPaymentProvider> logger) : IPaymentProvider
{
    /// <summary>
    /// Provider code. Matches <c>CountryConfiguration.DefaultPaymentProvider</c>
    /// for keyed-service selection, though in practice
    /// <c>PaymentProviderFactory</c> short-circuits to this provider from
    /// the <c>Payments:Dev:Enabled</c> flag rather than from country data —
    /// dev and production share the same seeded country rows.
    /// </summary>
    public const string ProviderCode = "dev";

    /// <summary>Marker every synthetic session reference starts with.</summary>
    public const string ProviderRefPrefix = "dev-";

    /// <summary>Payment-method label recorded on orders paid via the bypass.</summary>
    public const string PaymentMethodLabel = "dev-bypass";

    public string Code => ProviderCode;

    /// <summary>True when <paramref name="providerRef"/> was issued by this provider.</summary>
    public static bool IsDevProviderRef(string? providerRef) =>
        providerRef is not null
        && providerRef.StartsWith(ProviderRefPrefix, StringComparison.Ordinal);

    public Task<BusinessResult<PaymentSession>> CreatePaymentAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var confirmBase = options.Value.ConfirmBaseUrl?.Trim();
        if (!DevPaymentOptions.IsValidConfirmBaseUrl(confirmBase))
        {
            // Mirrors the Comgate adapter's misconfiguration surface: the
            // caller gets a Configuration error rather than a redirect to
            // a URL the browser cannot follow. ValidateOnStart should have
            // caught this at boot; defensive.
            logger.LogError(
                "DevPayment.CreatePayment: Payments:Dev:ConfirmBaseUrl is not an absolute http(s) URL or an origin-relative path ('{ConfirmBaseUrl}').",
                confirmBase);
            return Task.FromResult(BusinessResult.Failure<PaymentSession>(
                Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured)));
        }

        var providerRef = ProviderRefPrefix + Guid.NewGuid().ToString("N");
        // May be origin-relative — the frontend navigates with
        // window.location.assign, which resolves it against the current
        // page origin. That is deliberate: see DevPaymentOptions.ConfirmBaseUrl.
        var redirectUrl =
            $"{confirmBase!.TrimEnd('/')}/api/v1/orders/{Uri.EscapeDataString(order.Id)}" +
            $"/dev-payment/confirm?providerRef={Uri.EscapeDataString(providerRef)}";

        logger.LogWarning(
            "DevPayment.CreatePayment: PAYMENT GATEWAY BYPASSED for order {OrderId} ({AmountMinor} {Currency}). Synthetic ref={ProviderRef}.",
            order.Id, order.TotalAmountMinor, order.Currency, providerRef);

        return Task.FromResult(BusinessResult.Success(
            new PaymentSession(providerRef, redirectUrl)));
    }

    /// <summary>
    /// Always reports <see cref="PaymentState.Pending"/>. The bypass holds
    /// no session state of its own, and Pending is the answer that keeps
    /// <c>CreatePaymentSession</c>'s verify-then-recreate path correct: an
    /// unpaid order re-serves its cached redirect URL, while an order that
    /// has actually been paid is already rejected by that handler's
    /// <c>PendingPayment</c> state gate before this is ever consulted.
    /// </summary>
    public Task<BusinessResult<PaymentStatus>> VerifyPaymentAsync(
        string providerRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerRef))
            throw new ArgumentException("ProviderRef is required.", nameof(providerRef));

        return Task.FromResult(BusinessResult.Success(
            new PaymentStatus(PaymentState.Pending, PaymentMethodLabel, PaidAt: null)));
    }

    /// <summary>
    /// The bypass has no gateway and therefore no inbound webhook — the
    /// confirm endpoint dispatches <c>MarkOrderPaid</c> directly. A call
    /// here means a real gateway's webhook route resolved to this provider,
    /// which is a misconfiguration worth surfacing rather than swallowing.
    /// </summary>
    public Task<BusinessResult<WebhookPayload>> ParseAndVerifyWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            "DevPayment.ParseAndVerifyWebhook: the dev payment bypass has no webhook. " +
            "A gateway webhook reached a host configured with Payments:Dev:Enabled=true.");
        return Task.FromResult(BusinessResult.Failure<WebhookPayload>(
            Error.Configuration(BusinessErrorMessage.PaymentProviderMisconfigured)));
    }

    /// <summary>
    /// Settles instantly so the admin refund flow (T-0105) is walkable on
    /// dev. No money exists to move.
    /// </summary>
    public Task<BusinessResult<RefundReceipt>> RefundAsync(
        string providerRef,
        long amountMinor,
        string currency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerRef))
            throw new ArgumentException("ProviderRef is required.", nameof(providerRef));

        logger.LogWarning(
            "DevPayment.Refund: PAYMENT GATEWAY BYPASSED — synthetic refund of {AmountMinor} {Currency} on ref={ProviderRef}.",
            amountMinor, currency, providerRef);

        return Task.FromResult(BusinessResult.Success(new RefundReceipt(
            RefundProviderRef: ProviderRefPrefix + "refund-" + Guid.NewGuid().ToString("N"),
            AmountMinor: amountMinor,
            Currency: currency,
            RefundedAt: clock.UtcNow)));
    }
}
