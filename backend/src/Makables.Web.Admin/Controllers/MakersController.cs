using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.Admin;
using Makables.Core.AppServices.Features.Maker;
using Makables.Core.Domain.Auditing;
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
public sealed class MakersController(IAdminReadAuditWriter readAudit) : MakablesApiController
{
    /// <summary>Request body for <see cref="SetFeeOverride"/>. The maker id rides the route.</summary>
    public sealed record SetMakerFeeOverrideRequest(int? FeeRateOverrideBp, string Reason);

    /// <summary>Request body for the T-0034 judgment-call actions (<see cref="Verify"/> / <see cref="Deactivate"/> / <see cref="RefreshFromAres"/>).</summary>
    public sealed record MakerAdminActionRequest(string? Notes);

    /// <summary>
    /// Paged cross-tenant maker list (T-0119b / US-admin-0003..0005).
    /// Includes deactivated makers; one search box (company partial /
    /// exact IČO) + verification filter. LIST reads carry no audit row
    /// (ADR 0014 / T-0137 scope — low forensic value).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetAdminMakers.GetAdminMakersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetAdminMakers.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? isVerified = null,
        CancellationToken ct = default) =>
        HandleResult(await Mediator.Send(new GetAdminMakers.Query(page, pageSize, search, isVerified), ct));

    /// <summary>
    /// Single privileged maker header (T-0119b). Carries the account
    /// email — the successful read is audited (<c>maker.detail.view</c>)
    /// per the ADR 0014 read-side PII-disclosure carve-out (T-0137).
    /// </summary>
    [HttpGet("{makerId}")]
    [ProducesResponseType(typeof(GetAdminMakerDetail.GetAdminMakerDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string makerId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetAdminMakerDetail.Query(makerId), ct);

        if (result.IsSuccess)
        {
            await readAudit.AuditReadAsync(
                actionCode: "maker.detail.view",
                targetEntity: "maker",
                targetId: makerId,
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                notes: null,
                cancellationToken: ct);
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Mark the maker as verified (T-0034 / US-admin-0003). Idempotent;
    /// audited via <c>IAdminAuditableCommand</c>.
    /// </summary>
    [HttpPost("{makerId}/verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify(
        string makerId, [FromBody] MakerAdminActionRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new VerifyMaker.Command(makerId, request.Notes), ct));

    /// <summary>
    /// Soft-deactivate the maker (T-0034 / US-admin-0004) — hidden from
    /// the catalog, blocked from new orders; in-flight orders finish.
    /// </summary>
    [HttpPost("{makerId}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        string makerId, [FromBody] MakerAdminActionRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new DeactivateMaker.Command(makerId, request.Notes), ct));

    /// <summary>
    /// Re-fetch the maker's ARES snapshot (T-0034 / US-admin-0005).
    /// Returns whether the refreshed snapshot is still stale (registry
    /// outage → stale-cache fallback per ADR 0018).
    /// </summary>
    [HttpPost("{makerId}/refresh-ares")]
    [ProducesResponseType(typeof(RefreshMakerFromAres.RefreshMakerFromAresResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshFromAres(
        string makerId, [FromBody] MakerAdminActionRequest request, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new RefreshMakerFromAres.Command(makerId, request.Notes), ct));

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
