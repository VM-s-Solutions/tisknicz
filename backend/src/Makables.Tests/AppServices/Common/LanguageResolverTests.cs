using FluentAssertions;
using Makables.Core.AppServices.Common;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Configuration;
using Makables.Core.Domain.Identity;
using NSubstitute;

namespace Makables.Tests.AppServices.Common;

/// <summary>
/// Pins the language-resolution contract per T-0028 §"Language resolution":
/// <c>User.PreferredLanguage → CountryConfiguration.DefaultLanguageCode → "cs-CZ"</c>.
/// </summary>
public class LanguageResolverTests
{
    private readonly ICountryConfigurationRepository _countries =
        Substitute.For<ICountryConfigurationRepository>();
    private readonly LanguageResolver _sut;

    public LanguageResolverTests()
    {
        _sut = new LanguageResolver(_countries);
    }

    private static User CreateUser(string country, string? preferredLanguage = null)
    {
        var u = User.Create("user-1", "anna@example.cz", UserRole.Customer, "Anna", country,
            "argon2id$v=19$m=8192,t=1,p=1$AAAA$BBBB");
        if (preferredLanguage is not null) u.SetPreferredLanguage(preferredLanguage);
        return u;
    }

    private void ArrangeCountry(string code, string defaultLanguage)
    {
        var c = CountryConfiguration.Create(
            countryId: code,
            defaultCurrencyCode: "CZK",
            defaultLanguageCode: defaultLanguage,
            timeZoneId: "Europe/Prague",
            phonePrefix: "+420",
            dateFormat: "d. M. yyyy",
            standardVatRateBp: 2100,
            taxIdLabel: "DIČ",
            vatIdLabel: "DIČ",
            registrationNumberLabel: "IČO",
            defaultPaymentProvider: "comgate",
            defaultShippingCarrier: "packeta",
            defaultRegistry: "ares",
            defaultEmailProvider: "sendgrid");
        _countries.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(c);
    }

    [Fact]
    public async Task Returns_users_preferred_language_when_set()
    {
        ArrangeCountry("CZ", LanguageCode.CsCZ);
        var u = CreateUser("CZ", preferredLanguage: LanguageCode.EnUS);

        var result = await _sut.ResolveForUserAsync(u, CancellationToken.None);

        result.Should().Be(LanguageCode.EnUS);
    }

    [Fact]
    public async Task Falls_back_to_country_default_when_user_has_no_preferred_language()
    {
        ArrangeCountry("CZ", LanguageCode.CsCZ);
        var u = CreateUser("CZ");

        var result = await _sut.ResolveForUserAsync(u, CancellationToken.None);

        result.Should().Be(LanguageCode.CsCZ);
    }

    [Fact]
    public async Task Falls_back_to_platform_fallback_when_country_row_is_missing()
    {
        _countries.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);
        var u = CreateUser("XX"); // unmapped country

        var result = await _sut.ResolveForUserAsync(u, CancellationToken.None);

        result.Should().Be(LanguageCode.DefaultFallback);
    }

    [Fact]
    public async Task Resolution_is_pinned_in_order_user_then_country_then_fallback()
    {
        // Country says en-US, user says cs-CZ → user wins.
        ArrangeCountry("CZ", LanguageCode.EnUS);
        var u = CreateUser("CZ", preferredLanguage: LanguageCode.CsCZ);

        var result = await _sut.ResolveForUserAsync(u, CancellationToken.None);

        result.Should().Be(LanguageCode.CsCZ);
    }

    // T-0028 CQ reviewer N-3: the ResolveAsync(preferredLang, countryCode, ct)
    // overload exists so OneTimeTokenIssuer's no-user branch can probe the
    // resolver without minting a fake User aggregate. It MUST do the same
    // country lookup the User-bound overload does.

    [Fact]
    public async Task ResolveAsync_uses_explicit_preferred_language_when_valid()
    {
        ArrangeCountry("CZ", LanguageCode.CsCZ);

        var result = await _sut.ResolveAsync(LanguageCode.EnUS, "CZ", CancellationToken.None);

        result.Should().Be(LanguageCode.EnUS);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_country_when_preferred_is_null()
    {
        ArrangeCountry("CZ", LanguageCode.CsCZ);

        var result = await _sut.ResolveAsync(null, "CZ", CancellationToken.None);

        result.Should().Be(LanguageCode.CsCZ);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_country_when_preferred_is_malformed()
    {
        ArrangeCountry("CZ", LanguageCode.CsCZ);

        var result = await _sut.ResolveAsync("garbage", "CZ", CancellationToken.None);

        result.Should().Be(LanguageCode.CsCZ);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_platform_default_when_country_unknown()
    {
        _countries.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CountryConfiguration?)null);

        var result = await _sut.ResolveAsync(null, "ZZ", CancellationToken.None);

        result.Should().Be(LanguageCode.DefaultFallback);
    }

    [Fact]
    public async Task ResolveAsync_country_lookup_runs_unconditionally_when_preferred_is_invalid()
    {
        // Timing-equalization invariant proxy: the call count is identical
        // for the "no preferred language" path vs the "country alone" path.
        ArrangeCountry("CZ", LanguageCode.CsCZ);

        await _sut.ResolveAsync(null, "CZ", CancellationToken.None);
        await _sut.ResolveAsync("not-a-tag", "CZ", CancellationToken.None);

        await _countries.Received(2).GetByCodeAsync("CZ", Arg.Any<CancellationToken>());
    }
}
