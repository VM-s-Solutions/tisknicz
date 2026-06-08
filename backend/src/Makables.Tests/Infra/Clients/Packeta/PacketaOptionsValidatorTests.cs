using FluentAssertions;
using Makables.Infra.Clients.Packeta;
using Microsoft.Extensions.Options;

namespace Makables.Tests.Infra.Clients.Packeta;

/// <summary>
/// T-0070 sec/ops M-4 analogue (mirror of <c>OutboxQueueOptionsValidator</c>
/// + <c>ComgateOptionsValidator</c>): a typo'd Packeta API key /
/// public-widget key / BaseUrl / WidgetScriptUrl must fail the host at
/// boot, not silently inside the first <c>CreateShipmentAsync</c> call.
/// The validator runs as <see cref="IValidateOptions{TOptions}"/> with
/// <c>ValidateOnStart</c> so a misconfigured deploy crashes the worker
/// loudly during startup instead of producing 5xx responses to the maker
/// hours later.
/// </summary>
public class PacketaOptionsValidatorTests
{
    private static PacketaOptions Valid() => new()
    {
        ApiKey = "valid-api-password",
        PublicWidgetKey = "valid-public-widget-key",
        SenderLabel = "makables-test",
        BaseUrl = "https://api.packeta.test",
        WidgetScriptUrl = "https://widget.packeta.test/v6/library.js",
    };

    [Fact]
    public void Validator_succeeds_for_valid_options()
    {
        var sut = new PacketaOptionsValidator();

        var result = sut.Validate(name: null, Valid());

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_fails_when_ApiKey_is_blank(string apiKey)
    {
        var sut = new PacketaOptionsValidator();
        var opts = Valid();
        opts.ApiKey = apiKey;

        var result = sut.Validate(name: null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKey");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_fails_when_PublicWidgetKey_is_blank(string publicWidgetKey)
    {
        var sut = new PacketaOptionsValidator();
        var opts = Valid();
        opts.PublicWidgetKey = publicWidgetKey;

        var result = sut.Validate(name: null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PublicWidgetKey");
    }

    [Theory]
    [InlineData("ftp://api.packeta.test")]
    [InlineData("api.packeta.test")]       // relative path / scheme-less
    [InlineData("not a url at all")]        // malformed
    public void Validator_fails_when_BaseUrl_is_not_absolute_http_or_https(string baseUrl)
    {
        var sut = new PacketaOptionsValidator();
        var opts = Valid();
        opts.BaseUrl = baseUrl;

        var result = sut.Validate(name: null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("BaseUrl");
    }

    [Theory]
    [InlineData("http://widget.packeta.test/v6/library.js")] // http, must be https
    [InlineData("not a url")]                                 // malformed
    public void Validator_fails_when_WidgetScriptUrl_is_not_absolute_https(string widgetScriptUrl)
    {
        var sut = new PacketaOptionsValidator();
        var opts = Valid();
        opts.WidgetScriptUrl = widgetScriptUrl;

        var result = sut.Validate(name: null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("WidgetScriptUrl");
    }

    [Fact]
    public void Validator_succeeds_when_optional_fields_use_defaults()
    {
        // Construct a PacketaOptions with only the required secrets set;
        // SenderLabel ("makables-cz"), BaseUrl (production endpoint), and
        // WidgetScriptUrl (the v6 production library URL) all use their
        // type-level defaults. TestMode default is false.
        var sut = new PacketaOptionsValidator();
        var opts = new PacketaOptions
        {
            ApiKey = "valid-api-password",
            PublicWidgetKey = "valid-public-widget-key",
        };

        var result = sut.Validate(name: null, opts);

        result.Succeeded.Should().BeTrue(
            "defaults on SenderLabel + BaseUrl + WidgetScriptUrl + TestMode are valid");
    }
}
