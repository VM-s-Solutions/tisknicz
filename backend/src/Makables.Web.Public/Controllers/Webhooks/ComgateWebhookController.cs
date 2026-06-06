using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Makables.Core.Domain.Payments;
using Makables.Web.Public.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Makables.Web.Public.Controllers.Webhooks;

/// <summary>
/// Comgate payment-gateway webhook endpoint per T-0066 / ADR 0016.
/// Receives form-urlencoded notifications when a customer payment changes
/// state at Comgate; verifies the source (three-layer posture), looks up
/// the order, and dispatches the appropriate state-transition command.
///
/// <para>
/// <b>Three-layer security posture</b> (ADR 0016 §"Webhook handling"):
/// <list type="number">
///   <item><description><see cref="ComgateWebhookIpAllowlistAttribute"/>
///     runs BEFORE model binding. Source IP must match
///     <c>Comgate:WebhookAllowedIps</c>; otherwise 401 with no body.</description></item>
///   <item><description>The adapter
///     (<see cref="IPaymentProvider.ParseAndVerifyWebhookAsync"/>)
///     re-fetches authoritative status from Comgate. The body's
///     <c>status</c> field is NEVER trusted.</description></item>
///   <item><description>The controller cross-checks the body's
///     <c>refId</c> against the order resolved via the authoritative
///     <c>transId</c>. Mismatch → 401 + Critical log (Q4); only a real
///     spoof or a Comgate bug would diverge.</description></item>
/// </list>
/// HMAC body-signature verification is intentionally NOT performed —
/// ADR 0016 doesn't mandate it; the sanctioned posture above is the
/// designed defence. See <c>docs/architecture/roles/payment-provider.md</c>.
/// </para>
///
/// <para>
/// <b>Idempotency.</b> Comgate retries webhooks until a 2xx arrives, so
/// every code path that's "already done" returns 200 without dispatching
/// the state-transition command. The webhook returns 4xx only for
/// genuine source-of-truth failures (allowlist, malformed body, refId
/// spoof).
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/webhooks/comgate")]
[AllowAnonymous]
[ComgateWebhookIpAllowlist]
[Consumes("application/x-www-form-urlencoded")]
public sealed class ComgateWebhookController(
    IPaymentProviderFactory providers,
    IOrderRepository orders,
    IMediator mediator,
    ILogger<ComgateWebhookController> logger) : MakablesApiController
{
    /// <summary>
    /// Country code the public host uses to resolve the payment provider.
    /// MVP ships a single CZ Comgate provider per <c>CountryConfiguration</c>.
    /// When multi-country support lands this becomes a per-request lookup
    /// keyed off the body's <c>country</c> field; out of scope for T-0066.
    /// </summary>
    private const string ProviderCountryCode = "CZ";

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Step 1: Resolve the country's payment provider. At MVP this is
        // a single keyed Comgate registration; the factory call keeps the
        // call site honest for the multi-country future.
        var providerResult = await providers.ResolveAsync(ProviderCountryCode, ct);
        if (!providerResult.IsSuccess)
        {
            // Config error (provider not registered for this country) —
            // a 500 is the right surface so ops sees the deploy break,
            // not a silent 200.
            logger.LogCritical(
                "Comgate webhook: failed to resolve payment provider for {CountryCode}. Error={Code}.",
                ProviderCountryCode, providerResult.Error!.Code);
            return HandleResult(providerResult);
        }
        var provider = providerResult.Value!;

        // Step 2: Parse + re-fetch via the adapter. The adapter owns the
        // form parse + the authoritative re-fetch in one place; the
        // controller never trusts the body's status field. Failures map
        // to typed BusinessResult — Validation → 400, Transient → 503,
        // Permanent → 422, Configuration → 500.
        var parseResult = await provider.ParseAndVerifyWebhookAsync(Request, ct);
        if (!parseResult.IsSuccess)
        {
            return HandleResult(parseResult);
        }
        var payload = parseResult.Value!;

        // Step 3: Lookup the order by the provider's reference.
        // Unscoped — this is the documented exception to ownership
        // scoping per IOrderRepository.GetByPaymentProviderRefAsync; the
        // network boundary (IP allowlist) holds the scope invariant
        // for the webhook host.
        var order = await orders.GetByPaymentProviderRefAsync(payload.ProviderRef, ct);
        if (order is null)
        {
            // Q3: 200 + Critical log. A 4xx would let Comgate retry-storm
            // on a real T-0065 ref-persistence bug; the Critical log gets
            // ops attention within minutes. The webhook itself moves on.
            logger.LogCritical(
                "Comgate webhook: no order found for providerRef={ProviderRef}. State={State}. Possible ref-persistence bug — ops investigation required.",
                payload.ProviderRef, payload.State);
            return Ok();
        }

        // Step 4: Spoof check on the body's refId vs the resolved order.
        // The body's refId is the one field a forged webhook could use
        // to redirect us to a different order than the one transId
        // belongs to. The form was already read by the adapter, so
        // re-reading via Request.Form is a cheap dictionary lookup.
        var bodyRefId = Request.Form["refId"].ToString();
        if (!string.Equals(order.Id, bodyRefId, StringComparison.Ordinal))
        {
            // Q4: 401 + Critical log. Refusing forces Comgate to retry
            // (correct behaviour if it's their bug); meanwhile ops sees
            // the alert. We do NOT mutate the order.
            logger.LogCritical(
                "Comgate webhook: refId mismatch. body.refId={BodyRefId}, order.Id={OrderId}, providerRef={ProviderRef}. Possible spoof — request refused.",
                bodyRefId, order.Id, payload.ProviderRef);
            // ErrorType.Unauthorized matches the 401 envelope per the
            // MakablesApiController.MapErrorToActionResult convention;
            // using Conflict here would silently mismatch type-vs-status.
            // T-0066 reviewer L-1.
            return Unauthorized(Error.Unauthorized(BusinessErrorMessage.PaymentWebhookRefIdMismatch));
        }

        // Step 5: Idempotency short-circuit. If the order already sits in
        // the target state for this inbound payload state, return 200
        // without dispatching the command. The handler's set-once
        // invariant would catch a duplicate transition too, but skipping
        // dispatch entirely avoids needless DB writes and audit-log noise.
        if (IsAlreadyInTargetState(order.State, payload.State))
        {
            logger.LogInformation(
                "Comgate webhook: order {OrderId} already in target state {OrderState} for inbound payload {PaymentState}. Idempotent no-op.",
                order.Id, order.State, payload.State);
            return Ok();
        }

        // Step 6: Switch on the payload's state and dispatch the matching
        // command. Only PAID has a T-0066 command (MarkOrderPaid); the
        // other states are logged-and-200'd until future tickets handle
        // them (T-0083 auto-cancel, T-0105 admin refund).
        switch (payload.State)
        {
            case PaymentState.Paid:
                var command = new MarkOrderPaid.Command(
                    OrderId: order.Id,
                    ProviderRef: payload.ProviderRef,
                    PaymentMethod: payload.PaymentMethod,
                    // Comgate's PaymentStatus carries PaidAt on PAID, but
                    // the webhook payload doesn't expose it on the
                    // controller's path — VerifyPaymentAsync returned it
                    // into the adapter and only PaymentMethod survives.
                    // T-0067 widens the WebhookPayload to carry PaidAt;
                    // at T-0066 we fall back to null and let the handler
                    // ignore it (T-0067 will persist).
                    PaidAt: null);

                // Step 7: Dispatch via Mediator. UoW pipeline behavior
                // commits. A race-loser (another webhook already
                // transitioned the same order) surfaces as
                // OrderInvalidTransition → 200 per ADR 0016:132.
                var dispatchResult = await mediator.Send(command, ct);
                if (!dispatchResult.IsSuccess)
                {
                    if (dispatchResult.Error!.Code == BusinessErrorMessage.OrderInvalidTransition)
                    {
                        logger.LogInformation(
                            "Comgate webhook: MarkOrderPaid lost the race for order {OrderId} (already transitioned). Idempotent 200.",
                            order.Id);
                        return Ok();
                    }
                    // Unexpected handler failure — surface via the
                    // normal mapping so a 500 reaches ops alerting.
                    logger.LogError(
                        "Comgate webhook: MarkOrderPaid returned unexpected failure for order {OrderId}. Code={Code}.",
                        order.Id, dispatchResult.Error.Code);
                    return HandleResult(dispatchResult);
                }
                break;

            case PaymentState.Cancelled:
            case PaymentState.Failed:
            case PaymentState.Refunded:
            case PaymentState.Authorized:
            case PaymentState.Pending:
                // No T-0066 state-machine action. Future tickets handle
                // these: T-0083 customer auto-cancel, T-0105 admin
                // refund. Log so the webhook traffic stays observable.
                logger.LogInformation(
                    "Comgate webhook: payload state {PaymentState} not actioned at T-0066 for order {OrderId}.",
                    payload.State, order.Id);
                break;

            default:
                // Defensive — every PaymentState above is covered.
                // A new enum value would surface here.
                logger.LogWarning(
                    "Comgate webhook: unhandled payload state {PaymentState} for order {OrderId}.",
                    payload.State, order.Id);
                break;
        }

        // Step 8: 200 on every non-failure path. The webhook only returns
        // 4xx for IP / spoof / malformed-body — never for "business
        // rejected".
        return Ok();
    }

    /// <summary>
    /// Returns true when the order's current state already equals the
    /// target state for the inbound payload. Used for the idempotency
    /// short-circuit so a re-delivered webhook does not dispatch a
    /// no-op command. Only the PAID branch has a target state at T-0066;
    /// future payload states (Cancelled / Refunded) extend this when
    /// their state-machine actions land.
    /// </summary>
    private static bool IsAlreadyInTargetState(OrderState orderState, PaymentState payloadState) =>
        payloadState switch
        {
            PaymentState.Paid => orderState
                is OrderState.Paid
                or OrderState.Accepted
                or OrderState.Shipped
                or OrderState.Delivered
                or OrderState.Completed
                or OrderState.Refunded,
            _ => false,
        };
}
