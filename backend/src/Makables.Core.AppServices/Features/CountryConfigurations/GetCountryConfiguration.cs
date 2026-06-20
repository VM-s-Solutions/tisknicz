using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using MediatR;

namespace Makables.Core.AppServices.Features.CountryConfigurations;

/// <summary>
/// Admin reads the per-country control-plane row for pre-fill (T-0127 /
/// Q-0029, the PRIORITY read that removes the PR-2 full-replace fence).
/// <c>GET /api/v1/country-configurations/{countryCode}</c> returns the
/// CURRENT config via the existing
/// <see cref="ICountryConfigurationRepository.GetByCodeAsync"/>
/// (<c>AsNoTracking</c>, the hot provider-resolution read path) projected
/// to <b>exactly</b> the field set
/// <see cref="UpdateCountryConfiguration.UpdateCountryConfigurationResponse"/>
/// echoes — so the admin form round-trips byte-for-byte (a PUT is a
/// full-replace; the GET must return what the PUT echoes or the form
/// drifts). Unknown code → 404 <c>countryConfiguration.notFound</c> (the
/// existing code, reused — no new <see cref="BusinessErrorMessage"/> entry).
///
/// <para>
/// Read-only; no audit row (ADR 0014 audits WRITES only). The FE pre-fills
/// SSR, downgrades the warning banner to an info note, and gates the
/// provider-retype modal on a DIFF of a <c>Default*Provider</c> form value
/// vs the loaded config (T-0118c AC-4/AC-5). <c>[Authorize]</c> admin
/// audience (ADR 0013).
/// </para>
/// </summary>
public static class GetCountryConfiguration
{
    public sealed record Query(string CountryCode) : IQuery<GetCountryConfigurationResponse>;

    /// <summary>
    /// Globally-unique name (NSwag PR #38 convention). The field set mirrors
    /// <see cref="UpdateCountryConfiguration.UpdateCountryConfigurationResponse"/>'s
    /// editable fields exactly so the admin form round-trips.
    /// </summary>
    public sealed record GetCountryConfigurationResponse(
        string CountryCode,
        int StandardVatRateBp,
        int? ReducedVatRateBp,
        InvoicingMode InvoicingMode,
        int PlatformFeeRateBp,
        long DefaultShippingPriceMinor,
        string DefaultPaymentProvider,
        string DefaultShippingCarrier,
        string DefaultRegistry,
        string DefaultEmailProvider);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.CountryCode)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(ICountryConfigurationRepository configs)
        : IRequestHandler<Query, BusinessResult<GetCountryConfigurationResponse>>
    {
        public async Task<BusinessResult<GetCountryConfigurationResponse>> Handle(
            Query query, CancellationToken cancellationToken)
        {
            // AsNoTracking read path (GetByCodeAsync) — this is a pure read,
            // never a mutation; the tracked GetByCodeForUpdateAsync is the
            // T-0108 write path's concern.
            var config = await configs.GetByCodeAsync(query.CountryCode, cancellationToken);
            if (config is null)
            {
                return BusinessResult.Failure<GetCountryConfigurationResponse>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            return BusinessResult.Success(new GetCountryConfigurationResponse(
                config.CountryId,
                config.StandardVatRateBp,
                config.ReducedVatRateBp,
                config.InvoicingMode,
                config.PlatformFeeRateBp,
                config.DefaultShippingPriceMinor,
                config.DefaultPaymentProvider,
                config.DefaultShippingCarrier,
                config.DefaultRegistry,
                config.DefaultEmailProvider));
        }
    }
}
