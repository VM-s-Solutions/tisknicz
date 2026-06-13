using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Payouts;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host payout-batch endpoints (US-admin-0007). <c>[Authorize]</c>
/// under the admin audience per ADR 0013 — a customer/maker JWT cannot
/// replay here. The create endpoint is the same code path the T-0104
/// weekly timer Function dispatches, keeping the admin "run batch now"
/// button and the cron on one writer (ADR 0014 audit).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payout-batches")]
[Authorize]
public sealed class PayoutBatchesController : MakablesApiController
{
    /// <summary>
    /// Create the weekly payout batch: claim every payout-eligible
    /// Delivered order for the default country into a new
    /// <c>Processing</c> batch, issue per-maker Fee invoices, build the
    /// bank CSV, and enqueue maker emails (T-0102a + T-0102b). Returns 200
    /// on both the created and the re-run (<c>AlreadyExisted = true</c>)
    /// paths (Silent-Success shape, US-admin-0007 AC-4). An empty week
    /// returns 409 <c>payoutBatch.empty</c>; a same-week re-run after
    /// completion returns 409 <c>payoutBatch.weekAlreadyProcessed</c>.
    /// </summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(CreatePayoutBatch.CreatePayoutBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        HandleResult(await Mediator.Send(new CreatePayoutBatch.Command(), ct));
}
