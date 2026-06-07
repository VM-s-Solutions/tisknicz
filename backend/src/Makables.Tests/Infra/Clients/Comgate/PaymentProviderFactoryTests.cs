using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Payments;
using Makables.Infra.Clients.Comgate;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    public PaymentProviderFactoryTests()
    {
        _comgate.Code.Returns("comgate");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPaymentProvider>("comgate", _comgate);
        _services = services.BuildServiceProvider();

        _sut = new PaymentProviderFactory(
            _services, _configs, _cache, NullLogger<PaymentProviderFactory>.Instance);
    }

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
}
