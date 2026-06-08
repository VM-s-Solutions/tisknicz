using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Shipping;
using Makables.Infra.Clients.Packeta;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.Packeta;

/// <summary>
/// Pins T-0070 <see cref="ShippingCarrierFactory"/>: country → carrier-code
/// lookup with IMemoryCache, keyed service resolution, configuration error
/// surfaces.
/// </summary>
public class ShippingCarrierFactoryTests
{
    private const string CountryCode = "CZ";

    private readonly ICountryConfigurationRepository _countries = Substitute.For<ICountryConfigurationRepository>();
    private readonly IShippingCarrier _packetaCarrier = Substitute.For<IShippingCarrier>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly IServiceProvider _services;
    private readonly ShippingCarrierFactory _sut;

    public ShippingCarrierFactoryTests()
    {
        _packetaCarrier.Code.Returns("packeta");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IShippingCarrier>("packeta", _packetaCarrier);
        _services = services.BuildServiceProvider();

        _sut = new ShippingCarrierFactory(
            _services, _countries, _cache,
            NullLogger<ShippingCarrierFactory>.Instance);
    }

    private static CountryConfiguration BuildConfig(string carrier = "packeta") =>
        CountryConfiguration.Create(
            countryId: CountryCode,
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: "cs-CZ",
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ", vatIdLabel: "DIČ DPH",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: carrier,
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid",
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000");

    [Fact]
    public async Task Resolve_with_valid_country_returns_packeta_carrier()
    {
        _countries.GetByCodeAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        var result = await _sut.ResolveAsync(CountryCode, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(_packetaCarrier);
    }

    [Fact]
    public async Task Resolve_with_null_country_returns_Configuration_error()
    {
        var result = await _sut.ResolveAsync("", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
    }

    [Fact]
    public async Task Resolve_with_unknown_country_returns_Configuration_error()
    {
        _countries.GetByCodeAsync("XX", Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.ResolveAsync("XX", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
    }

    [Fact]
    public async Task Resolve_with_unregistered_carrier_code_returns_Configuration_error()
    {
        _countries.GetByCodeAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(BuildConfig(carrier: "unknown-carrier"));

        var result = await _sut.ResolveAsync(CountryCode, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.ShippingCarrierConfigurationError);
    }

    [Fact]
    public async Task Resolve_caches_country_lookup()
    {
        _countries.GetByCodeAsync(CountryCode, Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        await _sut.ResolveAsync(CountryCode, CancellationToken.None);
        await _sut.ResolveAsync(CountryCode, CancellationToken.None);

        // Second call must hit the cache, not the repo.
        await _countries.Received(1).GetByCodeAsync(CountryCode, Arg.Any<CancellationToken>());
    }
}
