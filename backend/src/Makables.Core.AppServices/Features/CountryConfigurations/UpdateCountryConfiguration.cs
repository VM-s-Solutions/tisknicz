using FluentValidation;
using Makables.Core.AppServices.Abstractions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Makables.Core.AppServices.Features.CountryConfigurations;

/// <summary>
/// Admin updates the per-country control-plane row (T-0108 /
/// US-admin-0006): VAT rates, invoicing mode, platform fee, default
/// shipping price, and the four provider codes — atomically, with a
/// before/after audit row (ADR 0014). A bad write changes VAT math, fee
/// splits, and provider selection for every subsequent order in the
/// country, so two gates guard the provider path:
/// <list type="bullet">
///   <item><b>Unregistered-code rejection (AC-2):</b> a changed
///   <c>Default*Provider</c> must match a registered keyed-service code —
///   else <c>country.providerNotRegistered</c>.</item>
///   <item><b>Retype gate (AC-3):</b> any provider change requires
///   <c>ConfirmedProviderCode</c> to equal the new (payment-first) value —
///   else <c>country.providerConfirmationMismatch</c>.</item>
/// </list>
///
/// <para>
/// In-flight orders keep their cached provider refs — the response carries
/// an advisory <c>InFlightOrderCount</c> (WARN, don't block, Q-C). A true
/// no-op (all values unchanged) returns 200 without touching mutators; the
/// audit pipeline still writes the benign "attempted" row (Q-0021). No
/// <c>SaveChangesAsync</c> — the UoW pipeline commits the tracked entity +
/// the audit row.
/// </para>
/// </summary>
public static class UpdateCountryConfiguration
{
    public sealed record Command(
        string CountryCode,
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
        string Reason)
        : ICommand<UpdateCountryConfigurationResponse>, IAdminAuditableCommand
    {
        public string ActionCode => "country.update";
        public string TargetEntity => "countryConfiguration";
        public string TargetId => CountryCode;
        public string? Notes => Reason;
    }

    /// <summary>Globally-unique name (NSwag PR #38 convention).</summary>
    public sealed record UpdateCountryConfigurationResponse(
        string CountryCode,
        int StandardVatRateBp,
        int? ReducedVatRateBp,
        InvoicingMode InvoicingMode,
        int PlatformFeeRateBp,
        long DefaultShippingPriceMinor,
        string DefaultPaymentProvider,
        string DefaultShippingCarrier,
        string DefaultRegistry,
        string DefaultEmailProvider,
        int InFlightOrderCount,
        bool ProviderChanged);

    // === Pure predicates (T-0108, pinned red-first) ===

    /// <summary>True when any of the four provider fields changed vs the row.</summary>
    public static bool RequiresConfirmation(
        bool paymentChanged, bool shippingChanged, bool registryChanged, bool emailChanged) =>
        paymentChanged || shippingChanged || registryChanged || emailChanged;

    /// <summary>
    /// The retype gate (AC-3, Q-C). When payment changed, the confirmation
    /// must equal the NEW payment value (payment is the highest-stakes
    /// field). Otherwise it must equal the single changed field's new value.
    /// Null / mismatch → false.
    /// </summary>
    public static bool ConfirmationMatches(
        string? confirmedProviderCode,
        bool paymentChanged, string newPayment,
        bool shippingChanged, string newShipping,
        bool registryChanged, string newRegistry,
        bool emailChanged, string newEmail)
    {
        if (string.IsNullOrEmpty(confirmedProviderCode))
        {
            return false;
        }

        var expected =
            paymentChanged ? newPayment
            : shippingChanged ? newShipping
            : registryChanged ? newRegistry
            : emailChanged ? newEmail
            : null;

        return expected is not null
            && string.Equals(confirmedProviderCode, expected, StringComparison.Ordinal);
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.CountryCode)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .Length(2).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.StandardVatRateBp)
                .InclusiveBetween(0, 10_000).WithErrorCode(BusinessErrorMessage.MinValue);

            When(c => c.ReducedVatRateBp.HasValue, () =>
            {
                RuleFor(c => c.ReducedVatRateBp!.Value)
                    .InclusiveBetween(0, 10_000).WithErrorCode(BusinessErrorMessage.MinValue);
                RuleFor(c => c.ReducedVatRateBp!.Value)
                    .LessThanOrEqualTo(c => c.StandardVatRateBp)
                    .WithErrorCode(BusinessErrorMessage.MinValue);
            });

            RuleFor(c => c.PlatformFeeRateBp)
                .InclusiveBetween(0, 10_000).WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(c => c.DefaultShippingPriceMinor)
                .GreaterThanOrEqualTo(0).WithErrorCode(BusinessErrorMessage.MinValue);

            RuleFor(c => c.DefaultPaymentProvider)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.DefaultShippingCarrier)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.DefaultRegistry)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);
            RuleFor(c => c.DefaultEmailProvider)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(64).WithErrorCode(BusinessErrorMessage.MaxLength);

            RuleFor(c => c.InvoicingMode)
                .IsInEnum().WithErrorCode(BusinessErrorMessage.InvalidEnumValue);

            RuleFor(c => c.Reason)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithErrorCode(BusinessErrorMessage.Required)
                .MaximumLength(2000).WithErrorCode(BusinessErrorMessage.MaxLength);
        }
    }

    public sealed class Handler(
        IUserSessionProvider session,
        ICountryConfigurationRepository configs,
        IProviderRegistry providerRegistry,
        IOrderQueries orderQueries,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BusinessResult<UpdateCountryConfigurationResponse>>
    {
        public async Task<BusinessResult<UpdateCountryConfigurationResponse>> Handle(
            Command command, CancellationToken cancellationToken)
        {
            // 1. Fail-closed — a control-plane mutation must never be
            // attributed to "system" (RefundOrder precedent).
            if (string.IsNullOrEmpty(session.GetUserId()))
            {
                return BusinessResult.Failure<UpdateCountryConfigurationResponse>(Error.Unauthorized());
            }

            // 2. Load the TRACKED row (GetByCodeForUpdateAsync — the read-side
            // GetByCodeAsync is AsNoTracking for the hot provider-resolution
            // path, so mutating its result would never commit).
            var config = await configs.GetByCodeForUpdateAsync(command.CountryCode, cancellationToken);
            if (config is null)
            {
                return BusinessResult.Failure<UpdateCountryConfigurationResponse>(
                    Error.NotFound("countryCode", BusinessErrorMessage.CountryConfigurationNotFound));
            }

            // 3. Provider deltas.
            var paymentChanged = !Same(config.DefaultPaymentProvider, command.DefaultPaymentProvider);
            var shippingChanged = !Same(config.DefaultShippingCarrier, command.DefaultShippingCarrier);
            var registryChanged = !Same(config.DefaultRegistry, command.DefaultRegistry);
            var emailChanged = !Same(config.DefaultEmailProvider, command.DefaultEmailProvider);
            var providerChanged = RequiresConfirmation(
                paymentChanged, shippingChanged, registryChanged, emailChanged);

            // 4. No-op fast path (Silent Success, Q-0021). The audit pipeline
            // still writes the benign attempt row.
            var nothingChanged = !providerChanged
                && config.StandardVatRateBp == command.StandardVatRateBp
                && config.ReducedVatRateBp == command.ReducedVatRateBp
                && config.InvoicingMode == command.InvoicingMode
                && config.PlatformFeeRateBp == command.PlatformFeeRateBp
                && config.DefaultShippingPriceMinor == command.DefaultShippingPriceMinor;
            if (nothingChanged)
            {
                return BusinessResult.Success(BuildResponse(config, inFlightCount: 0, providerChanged: false));
            }

            // 5. Unregistered-code rejection (AC-2) — runs BEFORE the retype
            // gate so a garbage code returns the more actionable error. Only
            // CHANGED provider fields are checked (an admin editing VAT alone
            // isn't blocked by a pre-existing-but-deprecated code).
            if (paymentChanged && !IsRegistered(ProviderKind.Payment, command.DefaultPaymentProvider))
            {
                return ProviderNotRegistered("defaultPaymentProvider");
            }
            if (shippingChanged && !IsRegistered(ProviderKind.Shipping, command.DefaultShippingCarrier))
            {
                return ProviderNotRegistered("defaultShippingCarrier");
            }
            if (registryChanged && !IsRegistered(ProviderKind.Registry, command.DefaultRegistry))
            {
                return ProviderNotRegistered("defaultRegistry");
            }
            if (emailChanged && !IsRegistered(ProviderKind.Email, command.DefaultEmailProvider))
            {
                return ProviderNotRegistered("defaultEmailProvider");
            }

            // 6. Retype gate (AC-3, Q-C).
            if (providerChanged && !ConfirmationMatches(
                    command.ConfirmedProviderCode,
                    paymentChanged, command.DefaultPaymentProvider,
                    shippingChanged, command.DefaultShippingCarrier,
                    registryChanged, command.DefaultRegistry,
                    emailChanged, command.DefaultEmailProvider))
            {
                return BusinessResult.Failure<UpdateCountryConfigurationResponse>(
                    Error.Validation("confirmedProviderCode",
                        BusinessErrorMessage.CountryProviderConfirmationMismatch));
            }

            // 7. In-flight advisory (cheap-first: only when a provider changed).
            var inFlightCount = providerChanged
                ? await orderQueries.CountInFlightByCountryAsync(command.CountryCode, cancellationToken)
                : 0;
            if (providerChanged && inFlightCount > 0)
            {
                logger.LogInformation(
                    "UpdateCountryConfiguration: {Count} in-flight orders in {CountryCode} keep their cached provider refs.",
                    inFlightCount, command.CountryCode);
            }

            // 8. Apply mutators (idempotent for unchanged groups; the entity's
            // Argument* guards backstop any value the Validator let through).
            config
                .UpdateVatRates(command.StandardVatRateBp, command.ReducedVatRateBp)
                .UpdateInvoicingMode(command.InvoicingMode)
                .UpdatePlatformFeeRate(command.PlatformFeeRateBp)
                .UpdateDefaultShippingPrice(command.DefaultShippingPriceMinor)
                .UpdateProviders(
                    command.DefaultPaymentProvider,
                    command.DefaultShippingCarrier,
                    command.DefaultRegistry,
                    command.DefaultEmailProvider);

            // 9. The UoW pipeline commits the tracked entity + the audit row.
            return BusinessResult.Success(BuildResponse(config, inFlightCount, providerChanged));
        }

        private bool IsRegistered(ProviderKind kind, string code) =>
            providerRegistry.GetRegisteredCodes(kind).Contains(code);

        private static BusinessResult<UpdateCountryConfigurationResponse> ProviderNotRegistered(string field) =>
            BusinessResult.Failure<UpdateCountryConfigurationResponse>(
                Error.Validation(field, BusinessErrorMessage.CountryProviderNotRegistered));

        private static bool Same(string current, string incoming) =>
            string.Equals(current, incoming, StringComparison.Ordinal);

        private static UpdateCountryConfigurationResponse BuildResponse(
            CountryConfiguration config, int inFlightCount, bool providerChanged) =>
            new(
                config.CountryId,
                config.StandardVatRateBp,
                config.ReducedVatRateBp,
                config.InvoicingMode,
                config.PlatformFeeRateBp,
                config.DefaultShippingPriceMinor,
                config.DefaultPaymentProvider,
                config.DefaultShippingCarrier,
                config.DefaultRegistry,
                config.DefaultEmailProvider,
                inFlightCount,
                providerChanged);
    }
}
