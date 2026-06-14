using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Admin;
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
/// <c>/orders</c> route on the other hosts. Reads carry NO
/// <c>IAdminAuditableCommand</c> (ADR 0014 audits writes, not reads).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public sealed class AdminQueriesController : MakablesApiController
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
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(
            new GetAllOrders.Query(page, pageSize, state, country, makerId, customerEmail), ct));

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
