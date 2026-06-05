using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Orders;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Customer.Controllers;

/// <summary>
/// Customer-host order endpoints (US-customer-0010 / US-customer-0011).
/// First controller on the Customer host — sets the convention for
/// every Phase-4 customer endpoint that follows (T-0064 attachments,
/// T-0080 list, T-0082 detail, T-0083 cancel).
///
/// <para>
/// Per ADR 0005 / patterns §A.16 — JSON-only, audience-bound. A
/// maker JWT cannot reach this surface (audience policy in
/// <c>AddMakablesAuth</c>); an admin JWT can. The email-confirmed gate
/// runs as host middleware (<c>RequireEmailConfirmedMiddleware</c>) so
/// every authenticated endpoint here inherits the 403 path without
/// per-action plumbing.
/// </para>
///
/// <para>
/// <b>Adapter discipline.</b> Comgate session creation is NOT in
/// CreateOrder per user decision Q1 — the frontend navigates to
/// <c>/objednavka/&lt;orderId&gt;</c> after a successful POST and
/// triggers T-0065's <c>CreatePaymentSession</c> from that page. The
/// order persists in <see cref="OrderState.PendingPayment"/> in the
/// meantime; if Comgate is down the customer can retry inside the 24-hour
/// window (US-customer-0010 AC-3).
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController : MakablesApiController
{
    public sealed record CreateOrderRequest(
        string ProductId,
        int Quantity,
        ShippingMethod ShippingMethod,
        string? ZasilkovnaPickupPointId,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? CustomerNotes);

    // Controller-level wrapper to dodge the OpenAPI schema-name collision
    // pattern from ProductController.cs:49-58. Every Features/*/Xxx.Response
    // would emit as "Response" and NSwag picks whichever wins the
    // collision; wrapping into a unique top-level shape gives the spec a
    // stable schema name (CreateOrderResponse) without touching the CQRS
    // nesting convention.
    public sealed record CreateOrderResponse(
        string OrderId,
        string OrderNumber,
        long TotalPriceMinor,
        string Currency);

    /// <summary>
    /// Create a customer order in <see cref="OrderState.PendingPayment"/>.
    /// Returns the four fields the frontend uses to navigate to the
    /// order page and trigger T-0065's payment-session creation.
    ///
    /// <para>
    /// 401 is intentionally not declared via <see cref="ProducesResponseTypeAttribute"/>
    /// because the framework's challenge response (no body) precedes the
    /// handler — but the handler's own Unauthorized backstop (called
    /// directly from a non-controller path, e.g. a future cron) DOES
    /// surface a typed <see cref="Error"/>, so we keep the 401 declared.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateOrder.Command(
            ProductId: body.ProductId,
            Quantity: body.Quantity,
            ShippingMethod: body.ShippingMethod,
            ZasilkovnaPickupPointId: body.ZasilkovnaPickupPointId,
            CustomerName: body.CustomerName,
            CustomerEmail: body.CustomerEmail,
            CustomerPhone: body.CustomerPhone,
            CustomerNotes: body.CustomerNotes), ct);

        // Project the handler's nested Response into the controller-level
        // shape so the OpenAPI schema gets a unique top-level name (see
        // CreateOrderResponse remark above).
        return result.IsSuccess
            ? HandleResult(BusinessResult.Success(new CreateOrderResponse(
                result.Value!.OrderId,
                result.Value.OrderNumber,
                result.Value.TotalPriceMinor,
                result.Value.Currency)))
            : HandleResult(BusinessResult.Failure<CreateOrderResponse>(result.Error!));
    }
}
