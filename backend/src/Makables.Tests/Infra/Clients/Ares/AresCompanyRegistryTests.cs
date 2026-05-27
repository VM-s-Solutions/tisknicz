using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Infra.Clients.Ares;
using Makables.TestUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly.Registry;

namespace Makables.Tests.Infra.Clients.Ares;

/// <summary>
/// Pins the T-0032 AresCompanyRegistry contract: format gate → in-memory
/// cache → DB cache → HTTP → 404 / transient-with-stale-fallback /
/// permanent. The ARES IČO used throughout is a real Czech entity
/// (Avast Software s.r.o., IČO 27074358) so it passes the mod-11 gate.
/// </summary>
public class AresCompanyRegistryTests
{
    private const string ValidIco = "27074358";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly ICompanyRegistryCacheStore _cacheStore =
        Substitute.For<ICompanyRegistryCacheStore>();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly FakeClock _clock = new();
    private readonly AresCompanyRegistry _sut;

    public AresCompanyRegistryTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AresCompanyRegistry.HttpClientName)
            .Returns(_ => new HttpClient(_handler));

        var opts = Options.Create(new AresOptions
        {
            BaseUrl = "https://ares.test",
            RetryCount = 0,
            OverallTimeoutSeconds = 5,
            InMemoryCacheTtlMinutes = 60,
            DbCacheTtlHours = 24,
            StaleFallbackDays = 7,
        });

        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<HttpResponseMessage>(
            AresCompanyRegistry.HttpClientName,
            (builder, _) => { /* no-op: no retry */ });

        _sut = new AresCompanyRegistry(
            factory, registry, _cacheStore, _memoryCache, _clock, opts,
            NullLogger<AresCompanyRegistry>.Instance);
    }

    // ---- helpers ----

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static string ValidAresPayload() => """
    {
      "ico": "27074358",
      "obchodniJmeno": "Avast Software s.r.o.",
      "dic": "CZ27074358",
      "pravniForma": "112",
      "datumVzniku": "2006-09-04",
      "sidlo": {
        "nazevUlice": "Pikrtova",
        "cisloDomovni": 1737,
        "nazevObce": "Praha",
        "psc": 14000
      }
    }
    """;

    /// <summary>
    /// Builds a cache-entry whose payload matches the production CachedRecord
    /// DTO shape (flat fields, not the live CompanyRecord with its embedded
    /// Address aggregate). The adapter's deserializer reads this shape.
    /// </summary>
    private CompanyRegistryCacheEntry ExistingDbEntry(DateTimeOffset fetchedAt, DateTimeOffset expiresAt)
    {
        var payload = JsonSerializer.Serialize(new
        {
            RegistrationNumber = ValidIco,
            VatId = "CZ27074358",
            CompanyName = "Avast Software s.r.o.",
            LegalForm = "Společnost s ručením omezeným",
            Street = "Pikrtova",
            HouseNumber = "1737",
            City = "Praha",
            Zip = "14000",
            State = (string?)null,
            CountryCodeIso = "CZ",
            AuditCountryCode = "CZ",
            IncorporatedOn = new DateOnly(2006, 9, 4),
            IsActiveInRegistry = true,
            SourceRegistry = AresCompanyRegistry.ProviderCode,
            FetchedAt = fetchedAt,
        });
        return CompanyRegistryCacheEntry.Create(
            registryCode: AresCompanyRegistry.ProviderCode,
            registrationNumber: ValidIco,
            payloadJson: payload,
            fetchedAt: fetchedAt,
            expiresAt: expiresAt);
    }

    // ---- format gate ----

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]            // wrong length
    [InlineData("00000000")]           // mod-11 fails (sum=0 → expects checksum 1)
    public async Task Rejects_invalid_ico_format_without_any_IO(string ico)
    {
        var result = await _sut.LookupByRegistrationNumberAsync(ico, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.IcoFormatInvalid);
        result.Error.Type.Should().Be(ErrorType.Validation);
        _handler.CallCount.Should().Be(0);
        await _cacheStore.DidNotReceive().GetAsync(default!, default!, default);
    }

    // ---- happy paths ----

    [Fact]
    public async Task Happy_path_fetches_from_ARES_then_writes_to_DB_and_memory_caches()
    {
        _cacheStore.GetAsync(AresCompanyRegistry.ProviderCode, ValidIco, Arg.Any<CancellationToken>())
            .Returns((CompanyRegistryCacheEntry?)null);
        _handler.Response = Json(HttpStatusCode.OK, ValidAresPayload());

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var r = result.Value!;
        r.RegistrationNumber.Should().Be(ValidIco);
        r.CompanyName.Should().Be("Avast Software s.r.o.");
        r.VatId.Should().Be("CZ27074358");
        r.LegalForm.Should().Be("Společnost s ručením omezeným");
        r.IsActiveInRegistry.Should().BeTrue();
        r.IsStale.Should().BeFalse();
        r.RegisteredAddress.City.Should().Be("Praha");
        r.RegisteredAddress.Zip.Should().Be("14000");
        r.RegisteredAddress.CountryCodeIso.Should().Be("CZ");

        await _cacheStore.Received(1).UpsertAsync(
            AresCompanyRegistry.ProviderCode, ValidIco,
            Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());

        // Promoted into memory cache — second call must NOT hit HTTP again.
        _handler.CallCount.Should().Be(1);
        var again = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);
        again.IsSuccess.Should().BeTrue();
        _handler.CallCount.Should().Be(1, "the second lookup must be served by the in-memory cache");
    }

    [Fact]
    public async Task Fresh_DB_cache_row_is_served_without_HTTP()
    {
        var dbEntry = ExistingDbEntry(_clock.UtcNow.AddHours(-1), _clock.UtcNow.AddHours(23));
        _cacheStore.GetAsync(AresCompanyRegistry.ProviderCode, ValidIco, Arg.Any<CancellationToken>())
            .Returns(dbEntry);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsStale.Should().BeFalse();
        _handler.CallCount.Should().Be(0);
        await _cacheStore.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ---- HTTP failure paths ----

    [Fact]
    public async Task ARES_404_returns_NotFound_without_writing_caches()
    {
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
        await _cacheStore.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData((int)HttpStatusCode.InternalServerError)]
    [InlineData((int)HttpStatusCode.ServiceUnavailable)]
    [InlineData(429)]
    public async Task ARES_5xx_or_429_without_DB_cache_returns_Transient(int status)
    {
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        _handler.Response = new HttpResponseMessage((HttpStatusCode)status);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryTransient);
        result.Error.Type.Should().Be(ErrorType.Transient);
    }

    [Theory]
    [InlineData("missing nazevObce", """
    { "ico": "27074358", "obchodniJmeno": "X", "sidlo": { "nazevUlice": "Pikrtova", "cisloDomovni": 1, "psc": 14000 } }
    """)]
    [InlineData("missing psc", """
    { "ico": "27074358", "obchodniJmeno": "X", "sidlo": { "nazevUlice": "Pikrtova", "cisloDomovni": 1, "nazevObce": "Praha" } }
    """)]
    [InlineData("missing both nazevUlice and cisloDomovni", """
    { "ico": "27074358", "obchodniJmeno": "X", "sidlo": { "nazevObce": "Praha", "psc": 14000 } }
    """)]
    [InlineData("missing sidlo entirely", """
    { "ico": "27074358", "obchodniJmeno": "X" }
    """)]
    public async Task ARES_response_with_incomplete_sidlo_is_Permanent_failure(string scenario, string body)
    {
        // T-0032 CQ reviewer M-1: incomplete ARES sidlo is "unexpected
        // shape" per ADR 0018 §"Error classification" — must surface as
        // Permanent so admin can investigate, not silently flow into the
        // Maker snapshot as literal "unknown" / "0" / "00000" placeholders.
        _ = scenario;
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        _handler.Response = Json(HttpStatusCode.OK, body);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryPermanent);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    [Fact]
    public async Task ARES_response_with_only_city_and_psc_succeeds_with_city_as_street_fallback()
    {
        // Mapper-allowed special case: a small-village or OSVČ-at-home
        // entity may omit `nazevUlice`. We still construct the Address
        // by using the city name as the street label. House number
        // can be 0 when ARES omits cisloDomovni.
        var body = """
        { "ico": "27074358", "obchodniJmeno": "X",
          "sidlo": { "cisloDomovni": 1, "nazevObce": "Praha", "psc": 14000 } }
        """;
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        _handler.Response = Json(HttpStatusCode.OK, body);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RegisteredAddress.Street.Should().Be("Praha");
        result.Value.RegisteredAddress.HouseNumber.Should().Be("1");
    }

    [Fact]
    public async Task Malformed_ARES_response_is_Permanent_failure()
    {
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        _handler.Response = Json(HttpStatusCode.OK, "{not valid json");

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryPermanent);
        result.Error.Type.Should().Be(ErrorType.Permanent);
    }

    // ---- stale fallback ----

    [Fact]
    public async Task ARES_transient_failure_with_stale_DB_entry_returns_IsStale_true()
    {
        // DB row expired 1 hour ago but fetched 3 days ago — inside the
        // 7-day stale window.
        var staleEntry = ExistingDbEntry(
            fetchedAt: _clock.UtcNow.AddDays(-3),
            expiresAt: _clock.UtcNow.AddHours(-1));
        _cacheStore.GetAsync(AresCompanyRegistry.ProviderCode, ValidIco, Arg.Any<CancellationToken>())
            .Returns(staleEntry);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsStale.Should().BeTrue();
        result.Value.CompanyName.Should().Be("Avast Software s.r.o.");
    }

    [Fact]
    public async Task ARES_transient_failure_with_TOO_old_DB_entry_does_NOT_serve_stale()
    {
        var tooOld = ExistingDbEntry(
            fetchedAt: _clock.UtcNow.AddDays(-10),   // outside the 7-day window
            expiresAt: _clock.UtcNow.AddDays(-9));
        _cacheStore.GetAsync(AresCompanyRegistry.ProviderCode, ValidIco, Arg.Any<CancellationToken>())
            .Returns(tooOld);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryTransient);
    }

    [Fact]
    public async Task ARES_permanent_failure_does_NOT_serve_stale_DB_entry()
    {
        // Stale-fallback applies only to Transient failures (ADR 0018 §"Error
        // classification"); a Permanent (parse) failure should not paper
        // over with stale data.
        var staleEntry = ExistingDbEntry(
            fetchedAt: _clock.UtcNow.AddDays(-3),
            expiresAt: _clock.UtcNow.AddHours(-1));
        _cacheStore.GetAsync(AresCompanyRegistry.ProviderCode, ValidIco, Arg.Any<CancellationToken>())
            .Returns(staleEntry);
        _handler.Response = Json(HttpStatusCode.OK, "{not valid json");

        var result = await _sut.LookupByRegistrationNumberAsync(ValidIco, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(BusinessErrorMessage.CompanyRegistryPermanent);
    }

    [Fact]
    public async Task Cancellation_during_lookup_propagates()
    {
        _cacheStore.GetAsync(default!, default!, default!).ReturnsForAnyArgs((CompanyRegistryCacheEntry?)null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _handler.OnSend = (_, ct) => throw new OperationCanceledException(ct);

        var act = async () => await _sut.LookupByRegistrationNumberAsync(ValidIco, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- stub ----

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
            var onSend = OnSend;
            if (onSend is not null) return Task.FromResult(onSend(request, cancellationToken));
            return Task.FromResult(Response);
        }
    }
}
