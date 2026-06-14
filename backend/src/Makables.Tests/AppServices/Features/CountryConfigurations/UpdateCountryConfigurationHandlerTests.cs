using FluentAssertions;
using Makables.Core.AppServices.Features.CountryConfigurations;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.CountryConfigurations;

/// <summary>
/// Unit tests for the T-0108 <see cref="UpdateCountryConfiguration"/>
/// handler. The provider-confirmation-mismatch + unregistered-code
/// predicates were pinned red-first in
/// <c>ProviderChangeConfirmationTests</c>; these pin the handler wiring.
/// </summary>
public sealed class UpdateCountryConfigurationHandlerTests
{
    private const string CZ = "CZ";

    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly ICountryConfigurationRepository _configs = Substitute.For<ICountryConfigurationRepository>();
    private readonly IProviderRegistry _registry = Substitute.For<IProviderRegistry>();
    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();
    private readonly UpdateCountryConfiguration.Handler _sut;

    public UpdateCountryConfigurationHandlerTests()
    {
        _session.GetUserId().Returns("admin-1");
        _registry.GetRegisteredCodes(ProviderKind.Payment).Returns(Set("comgate", "stripe"));
        _registry.GetRegisteredCodes(ProviderKind.Shipping).Returns(Set("packeta"));
        _registry.GetRegisteredCodes(ProviderKind.Registry).Returns(Set("ares"));
        _registry.GetRegisteredCodes(ProviderKind.Email).Returns(Set("sendgrid"));
        _orders.CountInFlightByCountryAsync(CZ, Arg.Any<CancellationToken>()).Returns(3);
        _sut = new UpdateCountryConfiguration.Handler(
            _session, _configs, _registry, _orders, NullLogger<UpdateCountryConfiguration.Handler>.Instance);
    }

    private static IReadOnlySet<string> Set(params string[] codes) =>
        new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

    private static CountryConfiguration SeedConfig() =>
        CountryConfiguration.Create(
            countryId: CZ,
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ DPH",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000",
            reducedVatRateBp: 1500,
            platformFeeRateBp: 1500,
            defaultShippingPriceMinor: 7900);

    private UpdateCountryConfiguration.Command CommandFrom(
        CountryConfiguration c,
        int? standardVat = null,
        string? payment = null,
        string? confirm = null) =>
        new(
            CountryCode: CZ,
            StandardVatRateBp: standardVat ?? c.StandardVatRateBp,
            ReducedVatRateBp: c.ReducedVatRateBp,
            InvoicingMode: c.InvoicingMode,
            PlatformFeeRateBp: c.PlatformFeeRateBp,
            DefaultShippingPriceMinor: c.DefaultShippingPriceMinor,
            DefaultPaymentProvider: payment ?? c.DefaultPaymentProvider,
            DefaultShippingCarrier: c.DefaultShippingCarrier,
            DefaultRegistry: c.DefaultRegistry,
            DefaultEmailProvider: c.DefaultEmailProvider,
            ConfirmedProviderCode: confirm,
            Reason: "Roční revize konfigurace.");

    [Fact]
    public async Task Provider_change_without_matching_confirmation_is_rejected()
    {
        var config = SeedConfig();
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _sut.Handle(CommandFrom(config, payment: "stripe", confirm: null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryProviderConfirmationMismatch);
        config.DefaultPaymentProvider.Should().Be("comgate", "no mutator applied");
    }

    [Fact]
    public async Task Unregistered_provider_code_is_rejected_before_the_retype_gate()
    {
        var config = SeedConfig();
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns(config);

        // "adyen" is not registered AND not confirmed — must return
        // providerNotRegistered (more actionable), not the mismatch.
        var result = await _sut.Handle(CommandFrom(config, payment: "adyen", confirm: null), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryProviderNotRegistered);
        config.DefaultPaymentProvider.Should().Be("comgate");
    }

    [Fact]
    public async Task Happy_path_provider_change_with_correct_retype_succeeds()
    {
        var config = SeedConfig();
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _sut.Handle(CommandFrom(config, payment: "stripe", confirm: "stripe"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DefaultPaymentProvider.Should().Be("stripe");
        result.Value.ProviderChanged.Should().BeTrue();
        result.Value.InFlightOrderCount.Should().Be(3);
        config.DefaultPaymentProvider.Should().Be("stripe");
    }

    [Fact]
    public async Task Happy_path_vat_only_change_no_provider_gate()
    {
        var config = SeedConfig();
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _sut.Handle(CommandFrom(config, standardVat: 1900, confirm: null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderChanged.Should().BeFalse();
        result.Value.InFlightOrderCount.Should().Be(0, "the count is skipped when no provider changed");
        result.Value.StandardVatRateBp.Should().Be(1900);
        await _orders.DidNotReceive().CountInFlightByCountryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_op_all_values_unchanged_returns_success_without_mutation()
    {
        var config = SeedConfig();
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns(config);

        var result = await _sut.Handle(CommandFrom(config), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProviderChanged.Should().BeFalse();
        _registry.DidNotReceive().GetRegisteredCodes(Arg.Any<ProviderKind>());
        await _orders.DidNotReceive().CountInFlightByCountryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fail_closed_when_session_has_no_user()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(CommandFrom(SeedConfig()), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
        await _configs.DidNotReceive().GetByCodeForUpdateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Config_not_found_returns_not_found()
    {
        _configs.GetByCodeForUpdateAsync(CZ, Arg.Any<CancellationToken>()).Returns((CountryConfiguration?)null);

        var result = await _sut.Handle(CommandFrom(SeedConfig()), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
    }

    // === Validator ===

    [Fact]
    public void Reduced_vat_above_standard_is_rejected_by_validator()
    {
        var config = SeedConfig();
        var command = CommandFrom(config) with { ReducedVatRateBp = 2200, StandardVatRateBp = 2100 };

        var result = new UpdateCountryConfiguration.Validator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ReducedVatRateBp"));
    }

    [Fact]
    public void Platform_fee_above_10000bp_is_rejected_by_validator()
    {
        var command = CommandFrom(SeedConfig()) with { PlatformFeeRateBp = 10_001 };

        new UpdateCountryConfiguration.Validator().Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_provider_code_and_bad_reason_and_negative_shipping_are_rejected_by_validator()
    {
        var validator = new UpdateCountryConfiguration.Validator();
        var baseline = CommandFrom(SeedConfig());

        validator.Validate(baseline with { DefaultPaymentProvider = "" }).IsValid.Should().BeFalse();
        validator.Validate(baseline with { Reason = "" }).IsValid.Should().BeFalse();
        validator.Validate(baseline with { Reason = new string('x', 2001) }).IsValid.Should().BeFalse();
        validator.Validate(baseline with { DefaultShippingPriceMinor = -1 }).IsValid.Should().BeFalse();
    }
}
