using System.Net;
using System.Text;
using FluentAssertions;
using Makables.Core.Domain.Addresses;
using Makables.Core.Domain.Common;
using Makables.Infra.Clients.Mapbox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;

namespace Makables.Tests.Infra.Clients.Mapbox;

/// <summary>
/// Pins the Mapbox adapter contract per ADR 0010. Covers:
/// happy path (success → Coordinates extracted from feature.center
/// with [lng, lat] order), no-match (empty features), 4xx (Permanent),
/// 5xx (Transient), malformed JSON (Permanent), out-of-range coords
/// rejected at the Coordinates boundary.
/// </summary>
public class MapboxAddressGeocoderTests
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly MapboxAddressGeocoder _sut;

    public MapboxAddressGeocoderTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(MapboxAddressGeocoder.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new MapboxOptions
        {
            AccessToken = "pk.test",
            BaseUrl = "https://api.mapbox.test",
            AutocompleteLimit = 5,
            RetryCount = 0,
            OverallTimeoutSeconds = 5,
        });
        // Zero-retry registry pipeline so each test exercises exactly one
        // HTTP call. Registered under the same key the adapter looks up.
        var registry = new Polly.Registry.ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            MapboxAddressGeocoder.HttpClientName,
            (builder, _) => { /* no-op: no retry */ });
        _sut = new MapboxAddressGeocoder(factory, registry, opts, NullLogger<MapboxAddressGeocoder>.Instance);
    }

    private static Address ValidAddress() => Address.Create(
        id: "addr-1", street: "Karlovarská", houseNumber: "1",
        city: "Praha", zip: "150 00", countryCodeIso: "CZ", auditCountryCode: "CZ");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    // ---- Geocode ----

    [Fact]
    public async Task Geocode_returns_coordinates_with_lng_lat_order_from_first_feature()
    {
        // Mapbox returns [longitude, latitude]; the adapter must swap to (lat, lng).
        _handler.Response = Json(HttpStatusCode.OK, """
        { "features": [ { "place_name": "X", "center": [14.4213, 50.0875] } ] }
        """);

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Latitude.Should().Be(50.0875);
        result.Value.Longitude.Should().Be(14.4213);
    }

    [Fact]
    public async Task Geocode_returns_NoMatch_when_features_is_empty()
    {
        _handler.Response = Json(HttpStatusCode.OK, """{ "features": [] }""");

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderNoMatch);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.InternalServerError)]
    [InlineData((int)HttpStatusCode.ServiceUnavailable)]
    [InlineData(429)]
    [InlineData(408)]
    public async Task Geocode_5xx_429_408_surfaces_as_Transient(int status)
    {
        _handler.Response = new HttpResponseMessage((HttpStatusCode)status);

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderTransientFailure);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.BadRequest)]
    [InlineData((int)HttpStatusCode.Unauthorized)]
    [InlineData((int)HttpStatusCode.Forbidden)]
    public async Task Geocode_4xx_surfaces_as_Permanent(int status)
    {
        _handler.Response = new HttpResponseMessage((HttpStatusCode)status);

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderPermanentFailure);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task Geocode_malformed_json_is_Permanent_failure()
    {
        _handler.Response = Json(HttpStatusCode.OK, """{ not valid json""");

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderPermanentFailure);
    }

    [Fact]
    public async Task Geocode_out_of_range_coordinates_rejected_as_Permanent()
    {
        // Mapbox returned a "valid" feature but with lat 99 — the Coordinates
        // value-object will reject this at construction; the adapter should
        // surface it as a Permanent failure rather than crash.
        _handler.Response = Json(HttpStatusCode.OK, """
        { "features": [ { "place_name": "X", "center": [0, 99] } ] }
        """);

        var result = await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderPermanentFailure);
    }

    [Fact]
    public async Task Geocode_request_url_includes_country_filter_and_NEVER_carries_the_access_token()
    {
        // T-0031 sec reviewer B-1: token is sent as Authorization: Bearer
        // so OTel HttpClient instrumentation doesn't capture it into the
        // url.full span attribute that ships to App Insights.
        _handler.Response = Json(HttpStatusCode.OK, """{ "features": [] }""");

        await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        var url = _handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain("country=cz");
        url.Should().Contain("limit=1");
        url.Should().NotContain("access_token", "the token MUST NOT appear in the URL");
        url.Should().NotContain("pk.test", "the token MUST NOT appear in the URL");
    }

    [Fact]
    public async Task Geocode_request_carries_Bearer_authorization_header()
    {
        _handler.Response = Json(HttpStatusCode.OK, """{ "features": [] }""");

        await _sut.GeocodeAsync(ValidAddress(), CancellationToken.None);

        var auth = _handler.LastRequest!.Headers.Authorization;
        auth.Should().NotBeNull();
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("pk.test");
    }

    // ---- Autocomplete ----

    [Fact]
    public async Task Autocomplete_maps_features_into_suggestions()
    {
        _handler.Response = Json(HttpStatusCode.OK, """
        {
          "features": [
            {
              "place_name": "Karlovarská 1, 150 00 Praha, Česko",
              "text": "Karlovarská",
              "address": "1",
              "center": [14.4213, 50.0875],
              "context": [
                { "id": "postcode.123", "text": "150 00" },
                { "id": "place.456",    "text": "Praha" },
                { "id": "country.789",  "text": "Česko", "short_code": "cz" }
              ]
            }
          ]
        }
        """);

        var result = await _sut.AutocompleteAsync("Karlov", "CZ", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull().And.HaveCount(1);
        var s = result.Value![0];
        s.Label.Should().Be("Karlovarská 1, 150 00 Praha, Česko");
        s.Street.Should().Be("Karlovarská");
        s.HouseNumber.Should().Be("1");
        s.City.Should().Be("Praha");
        s.Zip.Should().Be("150 00");
        s.CountryCodeIso.Should().Be("CZ");
        s.Coordinates!.Latitude.Should().Be(50.0875);
        s.Coordinates.Longitude.Should().Be(14.4213);
    }

    [Theory]
    [InlineData("", "CZ")]
    [InlineData("   ", "CZ")]
    [InlineData("query", "")]
    [InlineData("query", "C")]    // wrong length
    [InlineData("query", "CZE")]  // wrong length
    public async Task Autocomplete_rejects_blank_or_malformed_inputs_without_calling_Mapbox(string q, string country)
    {
        var result = await _sut.AutocompleteAsync(q, country, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderInvalidInput);
        result.Error.Type.Should().Be(ErrorType.Validation);
        _handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Autocomplete_5xx_is_Transient()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.BadGateway);

        var result = await _sut.AutocompleteAsync("Karlov", "CZ", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.GeocoderTransientFailure);
    }

    [Fact]
    public async Task Autocomplete_request_url_carries_limit_and_country_filter_and_NEVER_carries_token()
    {
        _handler.Response = Json(HttpStatusCode.OK, """{ "features": [] }""");

        await _sut.AutocompleteAsync("Karlov", "CZ", CancellationToken.None);

        var url = _handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain("country=cz");
        url.Should().Contain("autocomplete=true");
        url.Should().Contain("limit=5");
        url.Should().NotContain("access_token");
        url.Should().NotContain("pk.test");
    }

    [Fact]
    public async Task Autocomplete_request_carries_Bearer_authorization_header()
    {
        _handler.Response = Json(HttpStatusCode.OK, """{ "features": [] }""");

        await _sut.AutocompleteAsync("Karlov", "CZ", CancellationToken.None);

        var auth = _handler.LastRequest!.Headers.Authorization;
        auth.Should().NotBeNull();
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("pk.test");
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _handler.OnSend = (req, ct) => throw new OperationCanceledException(ct);

        var act = async () => await _sut.AutocompleteAsync("q", "CZ", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? OnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (OnSend is not null) return Task.FromResult(OnSend(request, cancellationToken));
            return Task.FromResult(Response);
        }
    }
}
