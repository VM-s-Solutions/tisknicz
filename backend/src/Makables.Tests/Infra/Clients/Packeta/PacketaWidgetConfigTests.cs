using FluentAssertions;
using Makables.Infra.Clients.Packeta;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace Makables.Tests.Infra.Clients.Packeta;

/// <summary>
/// Pure-logic tests for <see cref="PacketaShippingCarrier.WidgetConfig"/>:
/// no I/O, deterministic, builds the config dictionary verbatim from the
/// options + the supplied locale/country.
/// </summary>
public class PacketaWidgetConfigTests
{
    private static PacketaShippingCarrier Build(
        string publicKey = "pub-key-1",
        string widgetScriptUrl = "https://widget.packeta.com/v6/www/js/library.js")
    {
        var opts = Options.Create(new PacketaOptions
        {
            ApiKey = "secret",
            PublicWidgetKey = publicKey,
            SenderLabel = "makables-cz",
            BaseUrl = "https://api.packeta.com",
            WidgetScriptUrl = widgetScriptUrl,
        });
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            PacketaShippingCarrier.HttpClientName,
            (b, _) => b.AddTimeout(TimeSpan.FromSeconds(1)));
        return new PacketaShippingCarrier(
            httpFactory, opts, registry,
            NullLogger<PacketaShippingCarrier>.Instance);
    }

    [Fact]
    public void Code_equals_packeta()
    {
        Build().Code.Should().Be("packeta");
    }

    [Fact]
    public void WidgetConfig_returns_PublicKey_and_ScriptUrl_from_options()
    {
        var sut = Build(publicKey: "pub-42", widgetScriptUrl: "https://w.example/script.js");

        var config = sut.WidgetConfig("cs-CZ", "CZ");

        config.PublicKey.Should().Be("pub-42");
        config.ScriptUrl.Should().Be("https://w.example/script.js");
    }

    [Fact]
    public void WidgetConfig_passes_locale_and_country_through_Options_dictionary()
    {
        var config = Build().WidgetConfig("en-US", "SK");

        config.Options["language"].Should().Be("en-US");
        config.Options["country"].Should().Be("SK");
    }
}
