using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Comgate;
using Makables.Infra.Clients.Dev;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.Comgate;

/// <summary>
/// Pins the T-0065 <see cref="PaymentProviderFactory"/> contract:
/// reads the country's <c>DefaultPaymentProvider</c>, resolves the
/// matching keyed <see cref="IPaymentProvider"/>, caches the country
/// lookup with a 5-minute TTL, and surfaces a typed Configuration /
/// NotFound failure for the misconfig paths.
/// </summary>
public class PaymentProviderFactoryTests
{
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly IPaymentProvider _comgate = Substitute.For<IPaymentProvider>();
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly PaymentProviderFactory _sut;

    private readonly IPaymentProvider _devProvider = Substitute.For<IPaymentProvider>();

    public PaymentProviderFactoryTests()
    {
        _comgate.Code.Returns("comgate");
        _devProvider.Code.Returns(DevPaymentProvider.ProviderCode);

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentProvider>("comgate", _comgate);
        _services = services.BuildServiceProvider();

        _sut = BuildFactory(_services, new DevPaymentOptions());
    }

    private PaymentProviderFactory BuildFactory(IServiceProvider services, DevPaymentOptions devOptions) =>
        new(services,
            _configs,
            _cache,
            Options.Create(devOptions),
            NullLogger<PaymentProviderFactory>.Instance);

    private static CountryConfiguration BuildConfig(
        string countryId = "CZ", string paymentProvider = "comgate") =>
        CountryConfiguration.Create(
            countryId: countryId,
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ DPH",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: paymentProvider,
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",    issuerName: "JVM YORE s.r.o.",    issuerIco: "00000000");

    [Fact]
    public async Task Resolve_returns_keyed_provider_for_configured_country()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        var result = await _sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(_comgate);
    }

    [Fact]
    public async Task Resolve_caches_country_lookup_on_repeated_calls()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        await _sut.ResolveAsync("CZ", CancellationToken.None);
        await _sut.ResolveAsync("CZ", CancellationToken.None);
        await _sut.ResolveAsync("CZ", CancellationToken.None);

        // First call hits the repo; subsequent calls hit the cache.
        await _configs.Received(1).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_returns_PaymentProviderNotRegistered_when_provider_code_is_unknown()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig(paymentProvider: "stripe"));

        var result = await _sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderNotRegistered);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task Resolve_returns_CountryConfigurationNotFound_when_country_unknown()
    {
        _configs.GetByCodeAsync("ZZ", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.ResolveAsync("ZZ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Resolve_cache_is_per_country()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig(countryId: "CZ", paymentProvider: "comgate"));
        _configs.GetByCodeAsync("SK", Arg.Any<CancellationToken>())
            .Returns(BuildConfig(countryId: "SK", paymentProvider: "comgate"));

        await _sut.ResolveAsync("CZ", CancellationToken.None);
        await _sut.ResolveAsync("SK", CancellationToken.None);
        await _sut.ResolveAsync("CZ", CancellationToken.None);  // cached
        await _sut.ResolveAsync("SK", CancellationToken.None);  // cached

        await _configs.Received(1).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
        await _configs.Received(1).GetByCodeAsync("SK", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_returns_CountryConfigurationNotFound_for_blank_country_code()
    {
        var result = await _sut.ResolveAsync("  ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
    }

    // === Dev payment bypass ===

    [Fact]
    public async Task Resolve_returns_dev_provider_and_ignores_country_config_when_bypass_enabled()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentProvider>("comgate", _comgate);
        services.AddKeyedSingleton<IPaymentProvider>(DevPaymentProvider.ProviderCode, _devProvider);
        var sut = BuildFactory(
            services.BuildServiceProvider(),
            new DevPaymentOptions { Enabled = true, ConfirmBaseUrl = "http://localhost:5001" });

        var result = await sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be(DevPaymentProvider.ProviderCode);
        // The country row still says "comgate"; the bypass short-circuits
        // before the lookup, so it must not even be read.
        await _configs.DidNotReceive().GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_fails_loudly_when_bypass_enabled_but_dev_provider_not_registered()
    {
        // Fail closed: an enabled flag with no registration must never
        // silently fall back to charging through the real gateway.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentProvider>("comgate", _comgate);
        var sut = BuildFactory(
            services.BuildServiceProvider(),
            new DevPaymentOptions { Enabled = true, ConfirmBaseUrl = "http://localhost:5001" });
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var result = await sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.PaymentProviderNotRegistered);
        result.Error.Type.Should().Be(ErrorType.Configuration);
    }

    [Fact]
    public async Task Resolve_uses_country_config_when_bypass_disabled_even_if_dev_provider_registered()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentProvider>("comgate", _comgate);
        services.AddKeyedSingleton<IPaymentProvider>(DevPaymentProvider.ProviderCode, _devProvider);
        var sut = BuildFactory(services.BuildServiceProvider(), new DevPaymentOptions { Enabled = false });
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var result = await sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("comgate");
    }
}
