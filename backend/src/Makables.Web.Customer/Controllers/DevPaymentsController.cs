using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.AppServices.Common;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Infra.Clients.Dev;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Landing endpoint for the non-production payment bypass
/// (<see cref="DevPaymentProvider"/>). Stands in for the hosted gateway
/// page: the customer clicks "Zaplatit", <c>CreatePaymentSession</c> hands
/// back a redirect URL pointing here, the browser follows it, the order is
/// marked paid, and the browser lands back on the order detail page.
///
/// <para>
/// <b>Not part of the API contract.</b> <see cref="ApiExplorerSettingsAttribute"/>
/// keeps it out of the OpenAPI document, so the NSwag-generated client is
/// unaffected and CI contract parity holds. The frontend never calls it —
/// it only follows a URL the backend produced.
/// </para>
///
/// <para>
/// <b>Why GET.</b> The browser arrives here by top-level navigation, which
/// is a GET. That makes the endpoint non-idempotent-by-method, which is
/// only acceptable because every guard below is authorization- rather than
/// method-based, and because the underlying <see cref="MarkOrderPaid"/>
/// transition is itself idempotent (a replay loses the state gate and is
/// treated as an already-paid no-op).
/// </para>
///
/// <para>
/// <b>Four guards, all of which must pass:</b>
/// <list type="number">
///   <item><description><c>Payments:Dev:Enabled</c> must be on, else 404 —
///     on production the route answers as if it did not exist.</description></item>
///   <item><description><c>[Authorize]</c> plus a customer-scoped order
///     load: a caller can only ever settle their OWN order (the repository
///     returns null for a cross-customer id, which surfaces as 404 —
///     IDOR-shielded, same shape as every other order route).</description></item>
///   <item><description>The supplied reference must match the one persisted
///     on the order, so a guessed URL cannot settle an order whose session
///     was never created.</description></item>
///   <item><description>That reference must carry the
///     <see cref="DevPaymentProvider.ProviderRefPrefix"/> marker, so the
///     bypass can never settle a session that a REAL gateway issued.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Shape follows <c>ComgateWebhookController</c>: the payment callback
/// controllers do their own repository lookup for the ownership /
/// authenticity checks and then delegate the state transition to
/// <see cref="MarkOrderPaid"/> via Mediator, so the outbox emails, the
/// invoice-generate event and the aggregate invariants are identical to
/// the real gateway path.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders/{orderId}/dev-payment")]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevPaymentsController(
    IOptions<DevPaymentOptions> devPayments,
    IOptions<PublicAppUrlsOptions> publicAppUrls,
    IOrderRepository orders,
    IUserSessionProvider session,
    ILogger<DevPaymentsController> logger) : MakablesApiController
{
    [HttpGet("confirm")]
    public async Task<IActionResult> Confirm(
        string orderId,
        [FromQuery] string? providerRef,
        CancellationToken ct)
    {
        // Guard 1. Off by default; on production the section is absent, so
        // the route is indistinguishable from a typo'd URL.
        if (!devPayments.Value.Enabled)
        {
            return NotFound();
        }

        // Backstop — [Authorize] should have answered 401 already.
        var customerUserId = session.GetUserId();
        if (string.IsNullOrEmpty(customerUserId))
        {
            return Unauthorized(Error.Unauthorized());
        }

        // Guard 2. Customer-scoped load: null covers unknown-id AND
        // someone else's id.
        var order = await orders.GetByIdForCustomerAsync(orderId, customerUserId, ct);
        if (order is null)
        {
            return NotFound(Error.NotFound("orderId", BusinessErrorMessage.OrderNotFound));
        }

        // Guards 3 + 4. Ordinal comparison against the reference the
        // session actually reserved, and the dev-provider marker.
        if (string.IsNullOrWhiteSpace(providerRef)
            || !DevPaymentProvider.IsDevProviderRef(providerRef)
            || !string.Equals(order.PaymentProviderRef, providerRef, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "DevPayment.Confirm: reference mismatch for order {OrderId}. Supplied ref did not match the reserved dev session.",
                order.Id);
            return NotFound(Error.NotFound("providerRef", BusinessErrorMessage.OrderNotFound));
        }

        // Already settled — a refresh or a back-button replay. Bounce to
        // the order page rather than dispatching a doomed transition.
        if (order.State != OrderState.PendingPayment)
        {
            return RedirectToOrderPage(order.Id);
        }

        // PaidAt stays null so the handler stamps its own clock — there is
        // no external capture moment to preserve here.
        var result = await Mediator.Send(
            new MarkOrderPaid.Command(
                OrderId: order.Id,
                ProviderRef: providerRef,
                PaymentMethod: DevPaymentProvider.PaymentMethodLabel,
                PaidAt: null),
            ct);

        if (!result.IsSuccess)
        {
            // A concurrent confirm won the race and already moved the
            // order on — same benign outcome as the webhook's idempotent
            // 200. Anything else is a genuine failure worth surfacing to
            // whoever is testing, in the typed error envelope.
            if (result.Error!.Code == BusinessErrorMessage.OrderInvalidTransition)
            {
                return RedirectToOrderPage(order.Id);
            }

            logger.LogError(
                "DevPayment.Confirm: MarkOrderPaid failed for order {OrderId}. Code={Code}.",
                order.Id, result.Error.Code);
            return HandleResult(result);
        }

        logger.LogWarning(
            "DevPayment.Confirm: order {OrderId} marked PAID via the dev bypass — no money moved.",
            order.Id);
        return RedirectToOrderPage(order.Id);
    }

    /// <summary>
    /// Send the browser back to the customer-facing order detail page.
    /// <c>PublicAppUrls:WebBaseUrl</c> is validated at startup as an
    /// absolute https (or loopback http) URL, so this cannot become an
    /// open redirect; the id is still escaped as a path segment.
    /// </summary>
    private RedirectResult RedirectToOrderPage(string orderId) =>
        Redirect($"{publicAppUrls.Value.WebBaseUrl.TrimEnd('/')}/objednavka/{Uri.EscapeDataString(orderId)}");
}
