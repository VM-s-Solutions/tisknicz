using Makables.Core.Domain.Common;
using Makables.Core.Domain.Registry;
using Makables.Functions.Registry;
using Makables.Infra.Clients.Ares;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Makables.Tests.Functions.Registry;

/// <summary>
/// Pins T-0113 <see cref="EvictExpiredRegistryCacheFunction"/> as a thin
/// wrapper over <see cref="ICompanyRegistryCacheStore.EvictFetchedBeforeAsync"/>:
/// - the cutoff is <c>UtcNow - StaleFallbackDays</c> (same window the read
///   path accepts a stale row within, so the two never drift),
/// - a misconfigured 0/negative <c>StaleFallbackDays</c> is clamped to 1
///   (mirrors <c>AresCompanyRegistry</c>) so still-usable rows are never
///   evicted.
/// </summary>
public sealed class EvictExpiredRegistryCacheFunctionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-20T02:30:00Z");
    private static readonly TimerInfo Timer = new();

    private readonly ICompanyRegistryCacheStore _store = Substitute.For<ICompanyRegistryCacheStore>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<EvictExpiredRegistryCacheFunction> _logger =
        Substitute.For<ILogger<EvictExpiredRegistryCacheFunction>>();

    public EvictExpiredRegistryCacheFunctionTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private EvictExpiredRegistryCacheFunction Build(int staleFallbackDays)
    {
        var options = Options.Create(new AresOptions { StaleFallbackDays = staleFallbackDays });
        return new EvictExpiredRegistryCacheFunction(_store, options, _clock, _logger);
    }

    [Fact]
    public async Task Evicts_rows_fetched_before_now_minus_stale_fallback_window()
    {
        _store.EvictFetchedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(3);
        var sut = Build(staleFallbackDays: 7);

        await sut.RunAsync(Timer, CancellationToken.None);

        await _store.Received(1).EvictFetchedBeforeAsync(
            Now.AddDays(-7),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Clamps_non_positive_stale_fallback_to_one_day(int configuredDays)
    {
        var sut = Build(staleFallbackDays: configuredDays);

        await sut.RunAsync(Timer, CancellationToken.None);

        await _store.Received(1).EvictFetchedBeforeAsync(
            Now.AddDays(-1),
            Arg.Any<CancellationToken>());
    }
}
