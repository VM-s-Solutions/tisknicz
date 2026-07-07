using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin-host maker endpoints. <c>[Authorize]</c> under the admin audience
/// (ADR 0013 — a customer/maker JWT cannot replay here); every write
/// command implements <c>IAdminAuditableCommand</c> so the before/after
/// JSONB audit rides the pipeline (ADR 0014). Mirrors the
/// <c>OrdersController</c> / <c>UsersController</c> one-liner shape.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/makers")]
[Authorize]
public sealed class MakersController : MakablesApiController
{
    /// <summary>Request body for <see cref="SetFeeOverride"/>. The maker id rides the route.</summary>
    public sealed record SetMakerFeeOverrideRequest(int? FeeRateOverrideBp, string Reason);

    /// <summary>
    /// Admin sets or clears a maker's per-maker loyalty fee-rate override
    /// (T-0140 / US-admin-0018). Null <c>feeRateOverrideBp</c> clears the
    /// override (reverts to the country default). The value must be
    /// non-negative and must not exceed the maker's country's
    /// <c>CountryConfiguration.PlatformFeeRateBp</c> — a discount only,
    /// never above the advertised rate.
    /// </summary>
    [HttpPost("{makerId}/fee-override")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetFeeOverride(
        string makerId, [FromBody] SetMakerFeeOverrideRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new SetMakerFeeOverride.Command(makerId, request.FeeRateOverrideBp, request.Reason), ct));
}
