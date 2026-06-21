using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.Domain.Auditing;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Invoices;
using Makables.Core.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin cross-tenant read views (T-0111). Three paged, filtered,
/// <c>Unscoped()</c> list queries that double as the verification harness
/// for the bundle's three mutations. <c>[Authorize]</c> under the admin
/// audience (ADR 0013) — a customer/maker JWT cannot replay; that host
/// boundary is the security control for the unscoped reads. Flat resource
/// routes (<c>/admin-orders</c>, <c>/admin-invoices</c>, <c>/audit-log</c>)
/// disambiguate the admin cross-tenant view from the owner-scoped
/// <c>/orders</c> route on the other hosts. The paginated LIST reads carry NO
/// audit (ADR 0014: list reads would flood the table). The single-record
/// <c>GetOrder</c> detail read IS audited (<c>order.detail.view</c>) per the
/// ADR 0014 read-side PII-disclosure carve-out (T-0137 / Q-0028) — it returns
/// the un-redacted contact snapshot.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public sealed class AdminQueriesController(IAdminReadAuditWriter readAudit) : MakablesApiController
{
    /// <summary>
    /// Cross-tenant order list (US-admin-0009). Privileged row carries
    /// <c>customerEmail</c> + <c>makerName</c>; surfaces soft-deleted /
    /// anonymised rows with <c>isActive == false</c>.
    /// </summary>
    [HttpGet]
    [Route("api/v{version:apiVersion}/admin-orders")]
    [ProducesResponseType(typeof(GetAllOrders.GetAllOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderState? state = null,
        [FromQuery] string? country = null,
        [FromQuery] string? makerId = null,
        [FromQuery] string? customerEmail = null,
        [FromQuery] string? customerUserId = null,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetAllOrders.Query(page, pageSize, state, country, makerId, customerEmail, customerUserId), ct));

    /// <summary>
    /// Single privileged order header (T-0127 / Q-0024). Full breakdown +
    /// <c>customerEmail</c> + contact snapshot, no GDPR redaction; Unscoped
    /// (cross-tenant). 404 <c>order.notFound</c> for an unknown / inactive id.
    /// Replaces T-0118b's list-row-scan header.
    /// </summary>
    [HttpGet]
    [Route("api/v{version:apiVersion}/admin-orders/{orderId}")]
    [ProducesResponseType(typeof(GetAdminOrderDetail.GetAdminOrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(string orderId, CancellationToken ct = default)
    {
        var result = await Mediator.Send(new GetAdminOrderDetail.Query(orderId), ct);

        // T-0137 (Q-0028): audit the privileged PII read only when the order
        // actually resolved (a 404 is not a disclosure). The detail DTO carries
        // the full contact snapshot (CustomerEmail / ContactName / ContactPhone
        // / CustomerNotes) with no GDPR redaction — record who viewed whom.
        if (result.IsSuccess)
        {
            await readAudit.AuditReadAsync(
                actionCode: "order.detail.view",
                targetEntity: "order",
                targetId: orderId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                notes: null,
                cancellationToken: ct);
        }

        return HandleResult(result);
    }

    /// <summary>Cross-tenant invoice list (US-admin-0012).</summary>
    [HttpGet]
    [Route("api/v{version:apiVersion}/admin-invoices")]
    [ProducesResponseType(typeof(GetAllInvoices.GetAllInvoicesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] InvoiceType? type = null,
        [FromQuery] string? country = null,
        [FromQuery] string? recipient = null,
        [FromQuery] DateTimeOffset? dateFrom = null,
        [FromQuery] DateTimeOffset? dateTo = null,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetAllInvoices.Query(page, pageSize, type, country, recipient, dateFrom, dateTo), ct));

    /// <summary>Admin audit log (US-admin-0015). List omits before/after JSONB.</summary>
    [HttpGet]
    [Route("api/v{version:apiVersion}/audit-log")]
    [ProducesResponseType(typeof(GetAdminAuditLog.GetAdminAuditLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListAuditLog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? adminUserId = null,
        [FromQuery] string? actionCode = null,
        [FromQuery] string? targetEntity = null,
        [FromQuery] DateTimeOffset? dateFrom = null,
        [FromQuery] DateTimeOffset? dateTo = null,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetAdminAuditLog.Query(page, pageSize, adminUserId, actionCode, targetEntity, dateFrom, dateTo), ct));
}
