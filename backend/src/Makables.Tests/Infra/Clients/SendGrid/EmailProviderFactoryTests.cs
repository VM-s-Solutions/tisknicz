using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Email;
using Makables.Infra.Clients.SendGrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Makables.Tests.Infra.Clients.SendGrid;

/// <summary>
/// Pins the T-0124 <see cref="EmailProviderFactory"/> contract (mirrors
/// <see cref="Comgate.PaymentProviderFactoryTests"/>): reads the country's
/// <c>DefaultEmailProvider</c>, resolves the matching keyed
/// <see cref="IEmailProvider"/>, caches the country lookup with a 5-minute
/// TTL, and surfaces a typed Configuration / NotFound failure for the
/// misconfig paths.
/// </summary>
public class EmailProviderFactoryTests
{
    private readonly ICountryConfigurationRepository _configs =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly IEmailProvider _sendgrid = Substitute.For<IEmailProvider>();
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly EmailProviderFactory _sut;

    public EmailProviderFactoryTests()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEmailProvider>("sendgrid", _sendgrid);
        _services = services.BuildServiceProvider();

        _sut = new EmailProviderFactory(
            _services, _configs, _cache, NullLogger<EmailProviderFactory>.Instance);
    }

    private static CountryConfiguration BuildConfig(
        string countryId = "CZ", string emailProvider = "sendgrid") =>
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
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: emailProvider,
            issuerName: "JVM YORE s.r.o.",
            issuerIco: "00000000");

    [Fact]
    public async Task Resolve_returns_keyed_provider_for_configured_country()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        var result = await _sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(_sendgrid);
    }

    [Fact]
    public async Task Resolve_caches_country_lookup_on_repeated_calls()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig());

        await _sut.ResolveAsync("CZ", CancellationToken.None);
        await _sut.ResolveAsync("CZ", CancellationToken.None);
        await _sut.ResolveAsync("CZ", CancellationToken.None);

        await _configs.Received(1).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_returns_EmailProviderNotRegistered_when_provider_code_is_unknown()
    {
        _configs.GetByCodeAsync("CZ", Arg.Any<CancellationToken>())
            .Returns(BuildConfig(emailProvider: "ses"));

        var result = await _sut.ResolveAsync("CZ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.EmailProviderNotRegistered);
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
    public async Task Resolve_returns_CountryConfigurationNotFound_for_blank_country_code()
    {
        var result = await _sut.ResolveAsync("  ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CountryConfigurationNotFound);
    }
}
