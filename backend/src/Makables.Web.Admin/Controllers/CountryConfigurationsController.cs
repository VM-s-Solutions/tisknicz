using Asp.Versioning;
using Makables.Config.Controllers;
using Makables.Core.AppServices.Features.CountryConfigurations;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Makables.Web.Admin.Controllers;

/// <summary>
/// Admin country-configuration endpoint (T-0108 / US-admin-0006). Updates
/// the per-country control-plane row (VAT / fee / providers / invoicing
/// mode). <c>[Authorize]</c> under the admin audience (ADR 0013); the
/// command implements <c>IAdminAuditableCommand</c> so the before/after
/// JSONB + reason ride the pipeline (ADR 0014). A provider change requires
/// retyping the new (payment-first) code into <c>ConfirmedProviderCode</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/country-configurations")]
[Authorize]
public sealed class CountryConfigurationsController : MakablesApiController
{
    /// <summary>
    /// Read the country configuration for admin pre-fill (T-0127 / Q-0029).
    /// Returns exactly the editable field set <see cref="Update"/> echoes so
    /// the admin form round-trips. 404 <c>countryConfiguration.notFound</c>
    /// for an unseeded code. Read-only, admin-audience only (ADR 0013).
    /// </summary>
    [HttpGet("{countryCode}")]
    [ProducesResponseType(typeof(GetCountryConfiguration.GetCountryConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string countryCode, CancellationToken ct) =>
        HandleResult(await Mediator.Send(new GetCountryConfiguration.Query(countryCode), ct));

    /// <summary>Request body for <see cref="Update"/>. The country code rides the route.</summary>
    public sealed record UpdateCountryConfigurationRequest(
        int StandardVatRateBp,
        int? ReducedVatRateBp,
        InvoicingMode InvoicingMode,
        int PlatformFeeRateBp,
        long DefaultShippingPriceMinor,
        string DefaultPaymentProvider,
        string DefaultShippingCarrier,
        string DefaultRegistry,
        string DefaultEmailProvider,
        string? ConfirmedProviderCode,
        string Reason);

    /// <summary>
    /// Update the country configuration. Provider changes require the
    /// matching retype (400 <c>country.providerConfirmationMismatch</c>);
    /// unregistered codes are rejected (400 <c>country.providerNotRegistered</c>);
    /// the response carries an advisory <c>inFlightOrderCount</c>.
    /// </summary>
    [HttpPut("{countryCode}")]
    [ProducesResponseType(typeof(UpdateCountryConfiguration.UpdateCountryConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Error), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string countryCode,
        [FromBody] UpdateCountryConfigurationRequest request,
        CancellationToken ct) =>
        HandleResult(await Mediator.Send(
            new UpdateCountryConfiguration.Command(
                countryCode,
                request.StandardVatRateBp,
                request.ReducedVatRateBp,
                request.InvoicingMode,
                request.PlatformFeeRateBp,
                request.DefaultShippingPriceMinor,
                request.DefaultPaymentProvider,
                request.DefaultShippingCarrier,
                request.DefaultRegistry,
                request.DefaultEmailProvider,
                request.ConfirmedProviderCode,
                request.Reason),
            ct));
}
